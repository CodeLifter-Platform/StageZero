import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { CreateBucketInput, CustomDomainInput, R2Bucket } from '../../api/types';
import { server } from '../../test/server';
import { owner, renderApp, sessionInfo } from '../../test/render';
import { bucketNameProblem, domainProblem } from './StoragePage';

const options = { locationHints: ['wnam', 'enam', 'weur', 'eeur', 'apac', 'oc'], storageClasses: ['Standard', 'InfrequentAccess'], minTlsVersions: ['1.0', '1.1', '1.2', '1.3'] };

function signedIn(buckets: R2Bucket[]) {
  server.use(
    http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({ user: owner }))),
    http.get('/api/ip/current', () => HttpResponse.json({ ipAddress: null, lastChecked: null })),
    http.get('/api/storage/options', () => HttpResponse.json(options)),
    http.get('/api/storage/buckets', () => HttpResponse.json({ buckets })),
  );
}

const media: R2Bucket = { name: 'media', createdAt: '2026-10-01T00:00:00Z', location: 'weur', storageClass: 'Standard', jurisdiction: null };

describe('the storage page', () => {
  it('lists the buckets and creates one with the chosen hint', async () => {
    signedIn([media]);
    let sent: CreateBucketInput | undefined;
    server.use(http.post('/api/storage/buckets', async ({ request }) => {
      sent = await request.json() as CreateBucketInput;
      return HttpResponse.json({ ...media, name: sent.name, location: sent.locationHint });
    }));
    renderApp('/storage');

    expect(await screen.findByText('media')).toBeInTheDocument();
    expect(screen.getByText('Western Europe')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Create bucket' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText(/^Bucket name/), 'backups');
    await userEvent.selectOptions(within(dialog).getByLabelText(/^Location/), 'enam');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create bucket' }));

    await waitFor(() => expect(sent).toEqual({ name: 'backups', locationHint: 'enam', storageClass: null }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('refuses a bad bucket name before asking the server', async () => {
    signedIn([]);
    let posted = false;
    server.use(http.post('/api/storage/buckets', () => { posted = true; return HttpResponse.json(media); }));
    renderApp('/storage');

    await userEvent.click(await screen.findByRole('button', { name: 'Create bucket' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText(/^Bucket name/), 'My Bucket');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create bucket' }));

    expect(await within(dialog).findByText(/Lowercase letters, digits and hyphens/)).toBeInTheDocument();
    expect(posted).toBe(false);
  });

  it('attaches a custom domain to the selected bucket and shows its status', async () => {
    signedIn([media]);
    let sent: CustomDomainInput | undefined;
    const custom = [{ domain: 'cdn.example.com', enabled: true, zoneId: 'z1', zoneName: 'example.com', minTls: '1.2', ownershipStatus: 'active', sslStatus: 'pending' }];
    server.use(
      http.get('/api/storage/buckets/media/domains', () => HttpResponse.json({
        managed: { domain: 'pub-media.r2.dev', enabled: false },
        custom: sent ? custom : [],
        zones: [{ id: 'z1', name: 'example.com' }],
      })),
      http.post('/api/storage/buckets/media/domains/custom', async ({ request }) => {
        sent = await request.json() as CustomDomainInput;
        return HttpResponse.json(custom[0]);
      }),
    );
    renderApp('/storage');

    await userEvent.click(await screen.findByRole('button', { name: 'Domains' }));
    expect(await screen.findByText('pub-media.r2.dev', { exact: false })).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText(/^Hostname/), 'cdn.example.com');
    await userEvent.selectOptions(screen.getByLabelText(/^Minimum TLS/), '1.2');
    await userEvent.click(screen.getByRole('button', { name: 'Attach domain' }));

    await waitFor(() => expect(sent).toEqual({ domain: 'cdn.example.com', enabled: true, minTls: '1.2' }));
    expect(await screen.findByText('cdn.example.com')).toBeInTheDocument();
    expect(screen.getByText('TLS pending')).toBeInTheDocument();
  });

  it('points at Settings when the token has no account', async () => {
    server.use(
      http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({ user: owner }))),
      http.get('/api/ip/current', () => HttpResponse.json({ ipAddress: null, lastChecked: null })),
      http.get('/api/storage/options', () => HttpResponse.json(options)),
      http.get('/api/storage/buckets', () => HttpResponse.json({ title: 'The saved Cloudflare API token has no account permissions, which the tunnel and Access need.' }, { status: 409 })),
    );
    renderApp('/storage');

    expect(await screen.findByRole('alert')).toHaveTextContent('no account permissions');
    await userEvent.click(screen.getByRole('button', { name: 'Open Settings' }));
    expect(screen.getByTestId('location').textContent).toBe('/settings');
  });
});

describe('the storage rules the form checks first', () => {
  it('mirror the server on bucket names', () => {
    expect(bucketNameProblem('backups')).toBeNull();
    expect(bucketNameProblem('ab')).toContain('3 to 63');
    expect(bucketNameProblem('Backups')).toContain('Lowercase');
    expect(bucketNameProblem('-x-')).toContain('Lowercase');
  });

  it('mirror the server on hostnames', () => {
    expect(domainProblem('files.example.com')).toBeNull();
    expect(domainProblem('files')).toContain('full hostname');
    expect(domainProblem('')).toContain('required');
  });
});
