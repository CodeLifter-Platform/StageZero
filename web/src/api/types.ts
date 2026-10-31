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
  /** First run only: offer "Register this install with codelifter.net". */
  codeLifterRegistration: boolean;
  passwordResetCodeHeading: string;
  verificationCodeHeading: string;
}

export interface SetupResult { registeredWithCodeLifter: boolean }

/** Settings → Email from CodeLifter: the newsletter and StageZero updates, for the signed-in address. */
export interface EmailUpdates { enabled: boolean; email: string | null; choices: SignupChoice[] }

export interface EmailUpdateResult { sent: boolean; message: string }

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

/** One permission the Cloudflare token needs, phrased as Cloudflare's Create Token page shows it. */
export interface CloudflarePermission { scope: string; item: string; level: string; usedFor: string }

export interface CloudflarePermissionCheck { permission: CloudflarePermission; granted: boolean; detail: string }

/** What is saved for the Cloudflare token. The token itself never comes back from the server. */
export interface CloudflareCredentialStatus {
  isConfigured: boolean;
  /** False when a token is saved but the keys that decrypt it are gone. */
  isReadable: boolean;
  accountId: string | null;
  accountName: string | null;
  tokenId: string | null;
  expiresOn: string | null;
  verifiedAt: string | null;
  updatedAt: string | null;
}

export interface CloudflareCredential {
  status: CloudflareCredentialStatus;
  required: CloudflarePermission[];
  /** What the last save or check found; null on a plain read. */
  checks: CloudflarePermissionCheck[] | null;
}

/** Where the database this process runs on was chosen: nothing (SQLite), the web app, or the environment. */
export type DatabaseSource = 'default' | 'saved' | 'environment';

export interface DatabaseStatus {
  provider: 'sqlite' | 'postgres';
  source: DatabaseSource;
  /** Where the data is, never the password. */
  description: string;
  /** On the default SQLite file, so a move to PostgreSQL is offered. */
  canSwitch: boolean;
}

/** A PostgreSQL database as the form describes it. */
export interface PostgresTarget {
  host: string;
  port: number;
  database: string;
  username: string;
  password: string;
  sslMode: 'Prefer' | 'Require' | 'Disable';
}

export interface PostgresTestResult {
  ok: boolean;
  problem: string | null;
  serverVersion: string | null;
  tableCount: number;
  hasData: boolean;
  warnings: string[];
}

export interface DatabaseSwitchResult { description: string; restarting: boolean; copiedTables: string[] }

export interface DiscoveredServer { host: string; port: number; label: string }

export interface DockerAvailability { available: boolean; detail: string }

export interface ExistingContainer { name: string; status: string; running: boolean; hostPort: number | null }

/** What "Look for PostgreSQL" found on the machine. */
export interface PostgresDiscovery {
  servers: DiscoveredServer[];
  docker: DockerAvailability;
  /** StageZero's own PostgreSQL container, if one exists already. */
  container: ExistingContainer | null;
  suggestedPort: number;
  /** Host paths a new container would use. */
  dataFolder: string;
  backupFolder: string;
  inContainer: boolean;
}

export interface CreatePostgresInput {
  port: number;
  adminPassword: string;
  appPassword: string;
  backupFolder: string | null;
  publishOnAllInterfaces: boolean;
}

export interface CreatedPostgres {
  containerName: string;
  /** How StageZero reaches it. */
  host: string;
  port: number;
  /** The port published on the host, for tools. */
  hostPort: number;
  database: string;
  username: string;
  dataFolder: string;
  backupFolder: string;
}
