import type { AccessMode, RouteAccess } from '../api/types';

export const ACCESS_MODES: { value: AccessMode; label: string }[] = [
  { value: 'identity', label: 'Identity: people sign in' },
  { value: 'service_token', label: 'Service token: machines only' },
  { value: 'both', label: 'Both: people and machines' },
  { value: 'none', label: 'None: public, no policy' },
];

export const MODE_NAMES: Record<AccessMode, string> = {
  none: 'None (public)',
  identity: 'Identity',
  service_token: 'Service token',
  both: 'Identity + service token',
};

/** Which parts of the Access section a mode uses (AccessModes.Requires* on the server). */
export function accessFields(mode: AccessMode) {
  return {
    session: mode !== 'none',
    identity: mode === 'identity' || mode === 'both',
    serviceToken: mode === 'service_token' || mode === 'both',
  };
}

/** One per line in storage, a list in the picker. */
export const splitLines = (value: string | null | undefined) =>
  (value ?? '').split('\n').map((s) => s.trim()).filter(Boolean);

export const joinLines = (values: string[]) => (values.length ? values.join('\n') : null);

export const emptyAccess = (): RouteAccess => ({
  mode: 'identity',
  allowedEmails: null,
  allowedEmailDomains: null,
  allowedIdpIds: null,
  sessionDuration: '24h',
  createServiceToken: false,
  serviceTokenName: null,
  serviceTokenId: null,
  serviceTokenDuration: null,
});
