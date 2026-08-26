'use strict';

const http = require('node:http');
const { overwriteForwardingHeaders } = require('./socket-forwarding-headers.cjs');

const patchedMarker = Symbol.for('opendeepwiki.forwarding-headers-patched');

if (!http.Server.prototype[patchedMarker]) {
  const originalEmit = http.Server.prototype.emit;

  Object.defineProperty(http.Server.prototype, patchedMarker, {
    value: true,
    configurable: false,
    enumerable: false,
    writable: false,
  });

  http.Server.prototype.emit = function emit(event, ...args) {
    if (event === 'request') {
      overwriteForwardingHeaders(args[0]);
    }
    return originalEmit.call(this, event, ...args);
  };
}
