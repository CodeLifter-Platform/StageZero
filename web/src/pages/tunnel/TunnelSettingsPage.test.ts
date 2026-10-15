import { describe, expect, it } from 'vitest';
import { connectorInstructions, guessOs } from './TunnelSettingsPage';

describe('connector instructions', () => {
  it.each(['macos', 'windows', 'linux'] as const)('%s installs the service with the token', (os) => {
    expect(connectorInstructions(os, 'TOKEN123').commands).toContain('cloudflared service install TOKEN123');
  });

  it('docker passes the token to the compose tunnel profile', () => {
    expect(connectorInstructions('docker', 'TOKEN123').commands).toContain('TUNNEL_TOKEN=TOKEN123');
  });

  it.each([
    ['Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0)', 'macos'],
    ['Mozilla/5.0 (Windows NT 10.0; Win64; x64)', 'windows'],
    ['Mozilla/5.0 (X11; Linux x86_64)', 'linux'],
  ])('starts on the browser OS tab', (ua, os) => {
    expect(guessOs(ua)).toBe(os);
  });
});
