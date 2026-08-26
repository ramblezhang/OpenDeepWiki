import { createRequire } from 'node:module';
import { describe, expect, it } from 'vitest';

const require = createRequire(import.meta.url);
const { overwriteForwardingHeaders } = require('./socket-forwarding-headers.cjs');

describe('overwriteForwardingHeaders', () => {
  it('replaces spoofable forwarding headers with socket-derived values', () => {
    const request = {
      headers: {
        host: 'wiki.internal:8090',
        forwarded: 'for=203.0.113.42;proto=https',
        'x-forwarded-for': '203.0.113.42',
        'x-forwarded-host': 'evil.example',
        'x-forwarded-proto': 'https',
        'x-real-ip': '203.0.113.42',
      },
      socket: {
        encrypted: false,
        remoteAddress: '10.238.21.88',
      },
    };

    overwriteForwardingHeaders(request);

    expect(request.headers).toMatchObject({
      'x-forwarded-for': '10.238.21.88',
      'x-forwarded-host': 'wiki.internal:8090',
      'x-forwarded-proto': 'http',
      'x-real-ip': '10.238.21.88',
    });
    expect(request.headers).not.toHaveProperty('forwarded');
  });

  it('drops claimed client IPs when the socket address is unavailable', () => {
    const request = {
      headers: {
        'x-forwarded-for': '203.0.113.42',
        'x-real-ip': '203.0.113.42',
      },
      socket: {},
    };

    overwriteForwardingHeaders(request);

    expect(request.headers).not.toHaveProperty('x-forwarded-for');
    expect(request.headers).not.toHaveProperty('x-real-ip');
  });
});
