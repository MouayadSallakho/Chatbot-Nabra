// A deterministic local SMTP endpoint for failure drills. NON-PRODUCTION.
// Listens on 127.0.0.1 only. Never relays anything anywhere; it records how many
// messages reached DATA and how many were accepted, and exposes that over HTTP.
//
//   SMTP_MODE=accept   answer 250 after DATA (normal delivery)
//   SMTP_MODE=hang     accept the whole message, then never answer (the
//                      ambiguous case: the server may have taken it)
//   SMTP_MODE=slow     answer 250 after SMTP_DELAY_MS (default 20000)
//
// SMTP_PORT (2525), STATUS_PORT (2580): GET /status, POST /mode?m=accept|hang|slow, POST /reset

import { createServer } from 'node:net';
import { createServer as createHttp } from 'node:http';

let mode = process.env.SMTP_MODE || 'accept';
const delay = Number(process.env.SMTP_DELAY_MS || 20000);
const stats = { connections: 0, dataReceived: 0, accepted: 0 };

createServer((socket) => {
  stats.connections++;
  socket.write('220 local-smtp-stub ready\r\n');
  let inData = false;
  let buffer = '';
  socket.on('data', (chunk) => {
    buffer += chunk.toString('latin1');
    let index;
    while ((index = buffer.indexOf('\r\n')) >= 0) {
      const line = buffer.slice(0, index);
      buffer = buffer.slice(index + 2);
      if (inData) {
        if (line === '.') {
          inData = false;
          stats.dataReceived++;
          if (mode === 'hang') continue;
          const reply = () => {
            stats.accepted++;
            socket.write('250 2.0.0 queued\r\n');
          };
          mode === 'slow' ? setTimeout(reply, delay) : reply();
        }
        continue;
      }
      const verb = line.slice(0, 4).toUpperCase();
      if (verb === 'EHLO' || verb === 'HELO') socket.write('250-local-smtp-stub\r\n250 8BITMIME\r\n');
      else if (verb === 'MAIL' || verb === 'RCPT' || verb === 'RSET' || verb === 'NOOP') socket.write('250 OK\r\n');
      else if (verb === 'DATA') {
        inData = true;
        socket.write('354 end with .\r\n');
      } else if (verb === 'QUIT') {
        socket.end('221 bye\r\n');
      } else socket.write('250 OK\r\n');
    }
  });
  socket.on('error', () => {});
}).listen(Number(process.env.SMTP_PORT || 2525), '127.0.0.1');

createHttp((req, res) => {
  const url = new URL(req.url, 'http://x');
  if (url.pathname === '/mode') mode = url.searchParams.get('m') || mode;
  if (url.pathname === '/reset') Object.assign(stats, { connections: 0, dataReceived: 0, accepted: 0 });
  res.writeHead(200, { 'content-type': 'application/json' });
  res.end(JSON.stringify({ mode, ...stats }));
}).listen(Number(process.env.STATUS_PORT || 2580), '127.0.0.1');
