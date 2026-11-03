import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import type { CreateBucketInput, CreateCredentialInput, CustomDomainInput, R2Bucket, R2Object } from '../../api/types';
import { server } from '../../test/server';
import { owner, renderApp, sessionInfo } from '../../test/render';
import { credentialNameProblem } from './CredentialsCard';
import { folderNameProblem, pathForPrefix, prefixForPath } from './FilesPanel';
import { bucketNameProblem, domainProblem } from './StoragePage';

const options = { locationHints: ['wnam', 'enam', 'weur', 'eeur', 'apac', 'oc'], storageClasses: ['Standard', 'InfrequentAccess'], minTlsVersions: ['1.0', '1.1', '1.2', '1.3'], accessLevels: ['read', 'readwrite'] };

function signedIn(buckets: R2Bucket[]) {
  server.use(
    http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({ user: owner }))),
    http.get('/api/ip/current', () => HttpResponse.json({ ipAddress: null, lastChecked: null })),
    http.get('/api/storage/options', () => HttpResponse.json(options)),
    http.get('/api/storage/buckets', () => HttpResponse.json({ buckets })),
    http.get('/api/storage/credentials', () => HttpResponse.json({ credentials: [] })),
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

    await userEvent.click(await screen.findByRole('button', { name: 'Open' }));
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

describe('the files in a bucket', () => {
  const objects: Record<string, R2Object[]> = {
    '': [
      { name: 'photos/', key: 'photos/', isFolder: true, size: null, modified: null },
      { name: 'readme.txt', key: 'readme.txt', isFolder: false, size: 12, modified: '2026-11-02T12:00:00Z' },
    ],
    'photos/': [{ name: 'a.jpg', key: 'photos/a.jpg', isFolder: false, size: 2048, modified: '2026-11-02T12:00:00Z' }],
  };

  it('opens a bucket, walks into a folder, uploads into it and downloads from it', async () => {
    signedIn([media]);
    const uploads: Array<{ key: string; type: string | null; requestedWith: string | null; body: string }> = [];
    let signedFor: string | null = null;
    server.use(
      http.get('/api/storage/buckets/media/objects', ({ request }) => {
        const prefix = new URL(request.url).searchParams.get('prefix') ?? '';
        return HttpResponse.json({ prefix, entries: objects[prefix] ?? [], cursor: null });
      }),
      http.put('/api/storage/buckets/media/objects/*', async ({ request }) => {
        const key = decodeURIComponent(new URL(request.url).pathname.replace('/api/storage/buckets/media/objects/', ''));
        uploads.push({ key, type: request.headers.get('content-type'), requestedWith: request.headers.get('x-requested-with'), body: await request.text() });
        objects['photos/'] = [...(objects['photos/'] ?? []), { name: 'notes.txt', key, isFolder: false, size: 5, modified: '2026-11-02T13:00:00Z' }];
        return HttpResponse.json({ name: 'notes.txt', key, isFolder: false, size: 5, modified: '2026-11-02T13:00:00Z' }, { status: 201 });
      }),
      http.get('/api/storage/buckets/media/download', ({ request }) => {
        signedFor = new URL(request.url).searchParams.get('key');
        return HttpResponse.json({ url: 'https://acct.r2.cloudflarestorage.com/media/photos/a.jpg?X-Amz-Signature=x', expiresAt: '2026-11-02T13:10:00Z' });
      }),
    );
    const opened = vi.spyOn(window, 'open').mockImplementation(() => null);
    renderApp('/storage');

    await userEvent.click(await screen.findByRole('button', { name: 'Open' }));
    const files = await screen.findByRole('table', { name: 'Files in media' });
    expect(await within(files).findByText('photos/')).toBeInTheDocument();
    expect(within(files).getByText('readme.txt')).toBeInTheDocument();

    await userEvent.dblClick(within(files).getByText('photos/'));
    expect(await within(files).findByText('a.jpg')).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Path' })).toHaveTextContent('photos');

    // The picker input is visually hidden (pointer-events: none), so the change event is fired straight at it.
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [new File(['hello'], 'notes.txt', { type: 'text/plain' })] } });
    await waitFor(() => expect(uploads).toHaveLength(1));
    // jsdom's XMLHttpRequest stringifies a File body; a browser streams it. The key, type and CSRF header are what this checks.
    expect(uploads[0]).toMatchObject({ key: 'photos/notes.txt', type: 'text/plain', requestedWith: 'StageZero' });
    expect(await screen.findByText('Uploaded')).toBeInTheDocument();
    expect(await within(files).findByText('notes.txt')).toBeInTheDocument();

    await userEvent.dblClick(within(files).getByText('a.jpg'));
    await waitFor(() => expect(signedFor).toBe('photos/a.jpg'));
    expect(opened).toHaveBeenCalledWith('https://acct.r2.cloudflarestorage.com/media/photos/a.jpg?X-Amz-Signature=x', '_blank', 'noopener');
    opened.mockRestore();
  });

  it('deletes the selected files and folders after a confirmation', async () => {
    signedIn([media]);
    let sent: { keys: string[]; folders: string[] } | undefined;
    server.use(
      http.get('/api/storage/buckets/media/objects', () => HttpResponse.json({ prefix: '', entries: objects[''], cursor: null })),
      http.post('/api/storage/buckets/media/objects/delete', async ({ request }) => {
        sent = await request.json() as { keys: string[]; folders: string[] };
        return HttpResponse.json({ deleted: 3 });
      }),
    );
    // One user-event session, so the held Shift reaches the second click.
    const user = userEvent.setup();
    renderApp('/storage');

    await user.click(await screen.findByRole('button', { name: 'Open' }));
    const files = await screen.findByRole('table', { name: 'Files in media' });
    await user.click(await within(files).findByText('photos/'));
    await user.keyboard('{Shift>}');
    await user.click(within(files).getByText('readme.txt'));
    await user.keyboard('{/Shift}');
    const del = screen.getAllByRole('button', { name: /^Delete/ }).find((b) => b.textContent?.startsWith('Delete (')) as HTMLElement;
    expect(screen.getAllByRole('button', { name: /^Delete/ }).map((b) => b.textContent)).toEqual(['Delete', 'Delete (2)']);
    await userEvent.click(del);
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(sent).toEqual({ keys: ['readme.txt'], folders: ['photos/'] }));
    expect(await screen.findByText('3 objects deleted')).toBeInTheDocument();
  });

  it('maps prefixes to browser paths and back', () => {
    expect(pathForPrefix('')).toBe('/');
    expect(pathForPrefix('photos/2026/')).toBe('/photos/2026');
    expect(prefixForPath('/')).toBe('');
    expect(prefixForPath('/photos/2026')).toBe('photos/2026/');
    expect(folderNameProblem('2026')).toBeNull();
    expect(folderNameProblem('a/b')).toContain('slashes');
    expect(folderNameProblem('..')).toContain('cannot be named');
  });
});

describe('app credentials', () => {
  it('mints a credential for chosen buckets and shows its secrets exactly once', async () => {
    signedIn([media, { ...media, name: 'backups' }]);
    let sent: CreateCredentialInput | undefined;
    const credential = { id: 'token-1', name: 'Specter', status: 'active', access: 'readwrite', buckets: [{ name: 'backups', endpoint: 'https://acct.r2.cloudflarestorage.com' }], issuedOn: '2026-11-02T14:00:00Z', expiresOn: '2027-02-01T14:00:00Z', lastUsedOn: null };
    server.use(
      http.get('/api/storage/credentials', () => HttpResponse.json({ credentials: sent ? [credential] : [] })),
      http.post('/api/storage/credentials', async ({ request }) => {
        sent = await request.json() as CreateCredentialInput;
        return HttpResponse.json({ credential, token: 'cf-token-value', accessKeyId: 'token-1', secretAccessKey: 'deadbeef' }, { status: 201 });
      }),
    );
    renderApp('/storage');

    await userEvent.click(await screen.findByRole('button', { name: 'New credential' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText(/^Name/), 'Specter');
    await userEvent.click(within(dialog).getByLabelText('backups'));
    await userEvent.selectOptions(within(dialog).getByLabelText(/^Expires/), '90');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create credential' }));

    await waitFor(() => expect(sent).toEqual({ name: 'Specter', buckets: ['backups'], access: 'readwrite', expiresInDays: 90 }));
    const secrets = (await screen.findByText('This will not be shown again.')).closest('[role="dialog"]') as HTMLElement;
    expect(secrets).toHaveTextContent('Credential Specter');
    expect(within(secrets).getByLabelText('S3 access key ID')).toHaveValue('token-1');
    expect(within(secrets).getByLabelText('S3 secret access key')).toHaveValue('deadbeef');
    expect(within(secrets).getByLabelText('S3 endpoint')).toHaveValue('https://acct.r2.cloudflarestorage.com');
    expect(within(secrets).getByLabelText('Cloudflare API token')).toHaveValue('cf-token-value');
    expect(within(secrets).getByRole('button', { name: 'Done' })).toBeDisabled();
    await userEvent.click(within(secrets).getByLabelText('I have saved the secret'));
    await userEvent.click(within(secrets).getByRole('button', { name: 'Done' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    const table = await screen.findByRole('table', { name: 'Per-app R2 credentials' });
    expect(within(table).getByText('Specter')).toBeInTheDocument();
    expect(within(table).getByText('Read & write')).toBeInTheDocument();
    expect(screen.queryByText('deadbeef')).not.toBeInTheDocument();
  });

  it('says which permission to add when Cloudflare refuses the token list', async () => {
    signedIn([media]);
    server.use(http.get('/api/storage/credentials', () => HttpResponse.json({ title: 'Could not list the account\'s API tokens: Forbidden — Authentication error' }, { status: 502 })));
    renderApp('/storage');

    const notice = await screen.findByText('Credentials need one more permission');
    expect(notice.closest('[role="alert"]')).toHaveTextContent('API Tokens → Edit');
    expect(credentialNameProblem('')).toContain('required');
    expect(credentialNameProblem('x'.repeat(121))).toContain('120');
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
