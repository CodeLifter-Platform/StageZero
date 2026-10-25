// The one way the web app talks to the API. Same-origin, cookie session, JSON, and the
// X-Requested-With header the server demands on every state-changing call
// (StageZero/Api/ApiPipeline.cs). Failures become an ApiError whose message is the server's
// problem title: the server only ever puts text written for the operator there.
import type {
  AccessStatus, AccountResult, CloudflareCredential, CloudflareDnsRecord, CloudflareZone, CurrentIp, DeleteRouteResult,
  DnsProvider, DnsRecord, DnsRecordInput, IdentityProvider, ImportResult, IpGroup, RouteAccess,
  RoutesResult, SaveRouteResult, ServiceTokenInfo, SessionInfo, SessionUser, SetupResult, SetupView,
  TunnelConfig, TunnelRoute, TunnelRouteInput,
} from './types';

export class ApiError extends Error {
  readonly status: number;
  /** The server's trace reference for a 500, to find the failure in its log. */
  readonly reference: string | undefined;

  constructor(status: number, message: string, reference?: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.reference = reference;
  }

  /** The session is gone (signed out elsewhere, password changed, locked). */
  get isSignedOut(): boolean { return this.status === 401; }

  /** Signed in, but the account setup (verify email, replace password) isn't done. */
  get isAccountSetupRequired(): boolean { return this.status === 403; }
}

type Listener = (error: ApiError) => void;
const sessionListeners = new Set<Listener>();

/** Notified when any call finds the session gone or blocked, so the shell can send the user to sign in. */
export function onSessionLost(listener: Listener): () => void {
  sessionListeners.add(listener);
  return () => sessionListeners.delete(listener);
}

async function toError(response: Response): Promise<ApiError> {
  let title: string | undefined;
  let reference: string | undefined;
  try {
    const body = await response.json() as { title?: string; traceId?: string };
    title = body.title;
    reference = body.traceId;
  } catch {
    // Not a problem document (a proxy page, a dropped connection).
  }

  if (response.status >= 500 && response.status !== 502) {
    const message = 'StageZero hit an unexpected error. Your DNS records and tunnel routes are unaffected; '
      + 'try again, and if it keeps happening, the server log has the details.';
    return new ApiError(response.status, message, reference);
  }
  return new ApiError(response.status, title ?? `Request failed (${response.status}).`, reference);
}

export async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (method !== 'GET') headers['X-Requested-With'] = 'StageZero';
  if (body !== undefined) headers['Content-Type'] = 'application/json';

  let response: Response;
  try {
    response = await fetch(path, {
      method,
      headers,
      credentials: 'same-origin',
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError(0, "Can't reach StageZero. Check that the server is running.");
  }

  if (!response.ok) {
    const error = await toError(response);
    // Signing in is expected to 401 on a wrong password; that isn't a lost session.
    if ((error.isSignedOut || error.isAccountSetupRequired) && !path.startsWith('/api/auth/')) {
      sessionListeners.forEach((l) => l(error));
    }
    throw error;
  }

  if (response.status === 204) return undefined as T;
  return await response.json() as T;
}

const get = <T>(path: string) => request<T>('GET', path);
const post = <T>(path: string, body: unknown = {}) => request<T>('POST', path, body);
const put = <T>(path: string, body: unknown) => request<T>('PUT', path, body);
const del = <T>(path: string) => request<T>('DELETE', path);

export const api = {
  auth: {
    session: () => get<SessionInfo>('/api/auth/session'),
    setup: (email: string, password: string, optIns: string[]) => post<SetupResult>('/api/auth/setup', { email, password, optIns }),
    login: (email: string, password: string) => post<SessionUser>('/api/auth/login', { email, password }),
    logout: () => post<undefined>('/api/auth/logout'),
    forgotPassword: (email: string) => post<undefined>('/api/auth/forgot-password', { email }),
    resetPassword: (email: string, code: string, newPassword: string) =>
      post<undefined>('/api/auth/reset-password', { email, code, newPassword }),
    sendVerification: (email: string) => post<undefined>('/api/auth/account/send-verification', { email }),
    verifyEmail: (code: string) => post<SessionUser>('/api/auth/account/verify-email', { code }),
    changePassword: (currentPassword: string, newPassword: string) =>
      post<SessionUser>('/api/auth/account/change-password', { currentPassword, newPassword }),
  },
  ip: {
    current: () => get<CurrentIp>('/api/ip/current'),
    history: () => get<IpGroup[]>('/api/ip/history'),
    check: () => post<CurrentIp>('/api/ip/check'),
  },
  dns: {
    providers: () => get<DnsProvider[]>('/api/dns/providers'),
    addProvider: (name: string, zoneId: string, isActive: boolean) =>
      post<DnsProvider>('/api/dns/providers', { name, zoneId, isActive }),
    toggleProvider: (id: number) => post<DnsProvider>(`/api/dns/providers/${id}/toggle`),
    deleteProvider: (id: number) => del<undefined>(`/api/dns/providers/${id}`),
    addRecord: (providerId: number, input: DnsRecordInput) => post<DnsRecord>(`/api/dns/providers/${providerId}/records`, input),
    updateRecord: (id: number, input: DnsRecordInput) => put<DnsRecord>(`/api/dns/records/${id}`, input),
    deleteRecord: (id: number) => del<undefined>(`/api/dns/records/${id}`),
    zones: () => get<CloudflareZone[]>('/api/dns/zones'),
    cloudflareRecords: (providerId: number) => get<CloudflareDnsRecord[]>(`/api/dns/providers/${providerId}/cloudflare-records`),
    importAll: (providerId: number) => post<ImportResult>(`/api/dns/providers/${providerId}/import`),
  },
  tunnel: {
    config: () => get<{ config: TunnelConfig | null }>('/api/tunnel/config'),
    loadAccount: () => get<AccountResult>('/api/tunnel/account'),
    create: (zoneId: string, tunnelName: string) => post<SetupView>('/api/tunnel/create', { zoneId, tunnelName }),
    adopt: (zoneId: string, tunnelId: string) => post<SetupView>('/api/tunnel/adopt', { zoneId, tunnelId }),
    connectorToken: () => post<{ connectorToken: string }>('/api/tunnel/connector-token'),
    clear: () => del<undefined>('/api/tunnel/config'),
    routes: () => get<RoutesResult>('/api/tunnel/routes'),
    route: (id: number) => get<TunnelRoute>(`/api/tunnel/routes/${id}`),
    defaults: () => get<RouteAccess>('/api/tunnel/routes/defaults'),
    createRoute: (input: TunnelRouteInput) => post<SaveRouteResult>('/api/tunnel/routes', input),
    updateRoute: (id: number, input: TunnelRouteInput) => put<SaveRouteResult>(`/api/tunnel/routes/${id}`, input),
    toggleRoute: (id: number) => post<SaveRouteResult>(`/api/tunnel/routes/${id}/toggle`),
    deleteRoute: (id: number) => del<DeleteRouteResult>(`/api/tunnel/routes/${id}`),
    accessStatus: (id: number) => get<AccessStatus>(`/api/tunnel/routes/${id}/access-status`),
    identityProviders: () => get<IdentityProvider[]>('/api/tunnel/access/identity-providers'),
    serviceTokens: () => get<ServiceTokenInfo[]>('/api/tunnel/access/service-tokens'),
  },
  settings: {
    // The one Cloudflare token every feature uses. It goes in on save and never comes back.
    cloudflare: () => get<CloudflareCredential>('/api/settings/cloudflare'),
    saveCloudflare: (apiToken: string, accountId: string) =>
      put<CloudflareCredential>('/api/settings/cloudflare', { apiToken, accountId: accountId || null }),
    checkCloudflare: () => post<CloudflareCredential>('/api/settings/cloudflare/check'),
    removeCloudflare: () => del<undefined>('/api/settings/cloudflare'),
  },
};

/** Live IP changes over server-sent events. Returns a function that closes the stream. */
export function subscribeToIp(onIp: (ip: CurrentIp) => void): () => void {
  if (typeof EventSource === 'undefined') return () => {};
  const source = new EventSource('/api/ip/events');
  const handle = (event: MessageEvent<string>) => {
    try { onIp(JSON.parse(event.data) as CurrentIp); } catch { /* ignore a malformed event */ }
  };
  source.addEventListener('ip-current', handle as EventListener);
  source.addEventListener('ip-changed', handle as EventListener);
  return () => source.close();
}
