// Wire contracts for StageZero's API (StageZero/Api/*.cs). camelCase, enums as snake_case
// strings, times as ISO-8601 UTC.

export type AccessMode = 'none' | 'identity' | 'service_token' | 'both';

export interface SignupChoice { key: string; label: string; description: string }

export interface SessionUser {
  email: string;
  requiresPasswordChange: boolean;
  requiresEmailVerification: boolean;
  accountSetupRequired: boolean;
}

export interface SessionInfo {
  setupRequired: boolean;
  user: SessionUser | null;
  signupChoices: SignupChoice[];
  passwordResetCodeHeading: string;
  verificationCodeHeading: string;
}

export interface SetupResult { signup: { sent: boolean; message: string } | null }

export interface CurrentIp { ipAddress: string | null; lastChecked: string | null }

export interface IpGroup { ipAddress: string; lastCheckedAt: string; checkCount: number; firstSeenAt: string }

export interface DnsRecord {
  id: number;
  providerId: number;
  recordName: string;
  recordType: string;
  recordId: string | null;
  autoUpdate: boolean;
  autoUpdateSupported: boolean;
  autoUpdateUnavailableReason: string | null;
  content: string | null;
  lastIpAddress: string | null;
  lastUpdatedAt: string | null;
}

export interface DnsProvider {
  id: number;
  name: string;
  providerType: string;
  zoneId: string | null;
  isActive: boolean;
  createdAt: string;
  lastUsedAt: string | null;
  records: DnsRecord[];
}

export interface DnsRecordInput {
  recordName: string;
  recordType: string;
  autoUpdate: boolean;
  recordId?: string | null;
  content?: string | null;
}

export interface CloudflareZone { id: string; name: string }

export interface CloudflareDnsRecord { id: string; name: string; type: string; content: string }

export interface ImportResult { imported: number; skipped: number }

export interface TunnelConfig {
  accountId: string;
  zoneId: string | null;
  zoneName: string | null;
  tunnelId: string | null;
  tunnelName: string | null;
}

export interface TunnelInfo { id: string; name: string; isRemotelyManaged: boolean; status: string | null }

export interface AccountResult { zones: CloudflareZone[]; tunnels: TunnelInfo[] }

export interface SetupView { config: TunnelConfig; connectorToken: string; wasLocallyManaged: boolean }

export interface RouteAccess {
  mode: AccessMode;
  modeName?: string;
  allowedEmails: string | null;
  allowedEmailDomains: string | null;
  allowedIdpIds: string | null;
  sessionDuration: string | null;
  createServiceToken: boolean;
  serviceTokenName: string | null;
  serviceTokenId: string | null;
  serviceTokenDuration: string | null;
  syncedAt?: string | null;
}

export interface TunnelRoute {
  id: number;
  domainName: string;
  forwardScheme: string;
  forwardHost: string;
  forwardPort: number;
  forwardUrl: string;
  isEnabled: boolean;
  notes: string | null;
  access: RouteAccess;
  createdAt: string;
  updatedAt: string;
}

export interface RoutesResult { isConfigured: boolean; tunnelName: string | null; zoneName: string | null; routes: TunnelRoute[] }

export interface TunnelRouteInput {
  domainName: string;
  forwardScheme: string;
  forwardHost: string;
  forwardPort: number;
  isEnabled: boolean;
  notes: string | null;
  access: RouteAccess;
}

export interface NewServiceToken { name: string; clientId: string; clientSecret: string; expiresAt: string | null }

export interface SaveRouteResult { route: TunnelRoute; newServiceToken: NewServiceToken | null }

export interface DeleteRouteResult { serviceTokenRetainedReason: string | null; warnings: string[] }

export interface IdentityProvider { id: string; name: string; type: string }

export interface ServiceTokenInfo { id: string; name: string; clientId: string | null; duration: string | null; expiresAt: string | null }

export interface AccessStatus {
  hostname: string;
  configuredMode: AccessMode;
  application: {
    id: string;
    name: string;
    domain: string;
    type: string;
    sessionDuration: string | null;
    aud: string | null;
    allowedIdpIds: string[];
  } | null;
  policies: { id: string; name: string; decision: string; includeSummary: string[] }[];
  serviceToken: ServiceTokenInfo | null;
  serviceTokenCreatedByStageZero: boolean;
  serviceTokenRouteCount: number;
  warnings: string[];
  isUnprotected: boolean;
}

export type DatabaseProvider = 'sqlite' | 'postgres';

/** Who decided the database: nobody (the SQLite default), the Database page, or the environment. */
export type DatabaseSettingsSource = 'default' | 'settings' | 'environment';

export interface DatabaseStatus {
  provider: DatabaseProvider;
  /** Where the data is, never the password. */
  description: string;
  source: DatabaseSettingsSource;
  /** The page may switch: not the environment's choice, and no restart under way. */
  canChange: boolean;
  restartPending: boolean;
  sqliteFileExists: boolean;
  /** In a container, which the compose files restart on their own. */
  restartsAutomatically: boolean;
}

export interface ConnectionTest {
  host: string;
  port: number;
  database: string;
  username: string;
  serverVersion: string;
  /** False when the server is reached but the database is not there yet; the switch creates it. */
  databaseExists: boolean;
  isEmpty: boolean;
  hasStageZeroSchema: boolean;
}

export interface CopiedTable { table: string; rows: number }

export interface SwitchResult { message: string; copied: CopiedTable[] }
