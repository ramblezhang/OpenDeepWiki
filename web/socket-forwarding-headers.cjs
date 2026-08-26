'use strict';

/**
 * Replace client-controlled forwarding headers with values observed by the
 * public Node.js listener. The backend trusts this container as one proxy hop,
 * so forwarding an inbound X-Forwarded-For verbatim would allow IP spoofing.
 */
function overwriteForwardingHeaders(request) {
  const headers = request && request.headers;
  if (!headers) {
    return;
  }

  const remoteAddress = request.socket && request.socket.remoteAddress;
  if (typeof remoteAddress === 'string' && remoteAddress.length > 0) {
    headers['x-forwarded-for'] = remoteAddress;
    headers['x-real-ip'] = remoteAddress;
  } else {
    delete headers['x-forwarded-for'];
    delete headers['x-real-ip'];
  }

  // Do not let an RFC 7239 Forwarded header bypass the sanitized X-* values.
  delete headers.forwarded;

  const host = headers.host;
  if (typeof host === 'string' && host.length > 0) {
    headers['x-forwarded-host'] = host;
  } else {
    delete headers['x-forwarded-host'];
  }
  headers['x-forwarded-proto'] = request.socket && request.socket.encrypted ? 'https' : 'http';
}

module.exports = { overwriteForwardingHeaders };
