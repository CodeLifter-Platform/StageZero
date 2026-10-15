import { http, HttpResponse } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { server } from '../test/server';
import { ApiError, onSessionLost, request } from './client';

const problem = (status: number, title: string, traceId?: string) =>
  HttpResponse.json({ title, status, traceId }, { status, headers: { 'Content-Type': 'application/problem+json' } });

describe('request', () => {
  it('sends the request header on every state-changing call and not on reads', async () => {
    const seen: (string | null)[] = [];
    server.use(
      http.get('/api/x', ({ request: r }) => { seen.push(r.headers.get('X-Requested-With')); return HttpResponse.json({}); }),
      http.post('/api/x', ({ request: r }) => { seen.push(r.headers.get('X-Requested-With')); return HttpResponse.json({}); }),
    );

    await request('GET', '/api/x');
    await request('POST', '/api/x', {});

    expect(seen).toEqual([null, 'StageZero']);
  });

  it("shows the server's message for a refused request", async () => {
    server.use(http.post('/api/x', () => problem(409, "A route for 'app.example.com' already exists.")));

    await expect(request('POST', '/api/x', {})).rejects.toMatchObject({
      status: 409, message: "A route for 'app.example.com' already exists.",
    });
  });

  it('keeps Cloudflare\'s words on a 502, since they say what to fix', async () => {
    server.use(http.get('/api/x', () => problem(502, 'Failed to load zones: Authentication error')));

    await expect(request('GET', '/api/x')).rejects.toMatchObject({ message: 'Failed to load zones: Authentication error' });
  });

  it('turns a 500 into a calm message with the reference, never the server text', async () => {
    server.use(http.get('/api/x', () => problem(500, 'NullReferenceException at Foo.Bar', '00-abc-01')));

    const error = await request('GET', '/api/x').then(() => null, (e: unknown) => e as ApiError);
    if (!error) throw new Error('expected a failure');

    expect(error.message).toMatch(/unexpected error/);
    expect(error.message).not.toMatch(/NullReference/);
    expect(error.reference).toBe('00-abc-01');
  });

  it('reports a server that cannot be reached in words', async () => {
    server.use(http.get('/api/x', () => HttpResponse.error()));

    await expect(request('GET', '/api/x')).rejects.toMatchObject({ status: 0, message: expect.stringMatching(/Can't reach StageZero/) });
  });

  it('announces a lost session from an app call, but not a failed sign-in', async () => {
    const lost = vi.fn();
    const stop = onSessionLost(lost);
    server.use(
      http.get('/api/dns/providers', () => problem(401, 'Unauthorized')),
      http.post('/api/auth/login', () => problem(401, 'Invalid email or password')),
    );

    await request('GET', '/api/dns/providers').catch(() => {});
    await request('POST', '/api/auth/login', {}).catch(() => {});
    stop();

    expect(lost).toHaveBeenCalledTimes(1);
  });

  it('returns nothing for a 204', async () => {
    server.use(http.delete('/api/x', () => new HttpResponse(null, { status: 204 })));

    await expect(request('DELETE', '/api/x')).resolves.toBeUndefined();
  });
});
