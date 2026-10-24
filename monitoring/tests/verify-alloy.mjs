// Run against an official local Alloy binary; no Docker or production files.
// node monitoring/tests/verify-alloy.mjs /path/to/alloy
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { spawn } from 'node:child_process';

const executable = process.argv[2];
assert(executable, 'Pass the Alloy binary path');
const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'fc-alloy-check-'));
const logDirectory = path.join(temporary, 'logs');
fs.mkdirSync(logDirectory);
fs.mkdirSync(path.join(logDirectory, 'import'));
const now = new Date();
const yesterday = new Date(now.getTime() - 86400000);
const expired = new Date(now.getTime() - 15 * 86400000);
const line = (date, level, source, message) =>
  `${date.toISOString().replace('T', ' ').replace('Z', ' +00:00')} [${level}] [${source}] ${message}\n`;
fs.writeFileSync(path.join(logDirectory, 'server-log-live.txt'),
  line(now, 'INF', 'PipelineTest', '[RequestId:test-request] password=secret123 Bearer eyJabc.def.ghi\nException: sample\n  at Test.Frame\n' +
    '{"password":"secret value with spaces","access_token":"quoted-token"} password=\'single quoted secret\' StatusCode=500\n' +
    '<p>Your FunAndChecks email confirmation code:</p><p style="font-size: 24px; font-weight: bold; letter-spacing: 4px;">654321</p>\n' +
    '<p>Ваш код для сброса пароля в FunAndChecks:</p><p style="font-size: 24px; font-weight: bold; letter-spacing: 4px;">987654</p>') +
  line(expired, 'ERR', 'PipelineTest', 'expired event') +
  '2026-99-14 01:01:01.001 +00:00 [INF] [PipelineTest] invalid date\n');
fs.writeFileSync(path.join(logDirectory, 'import', 'server-log-snapshot.txt'),
  line(new Date(yesterday.getTime() + 3 * 3600000), 'WRN', 'OldPipelineTest', 'historical event')
    .replace('+00:00', '+03:00'));
fs.writeFileSync(path.join(logDirectory, 'admin-audit-live.jsonl'), JSON.stringify({
  timestamp: now.toISOString(), actorId: 42, actorName: 'Test Actor',
  actorRole: 'SuperAdmin', action: 'Grade.Update', objectType: 'Grade', objectId: 1,
  outcome: 'success', requestId: 'test-request', before: { value: 1 },
  after: { value: 2 }, kind: 'admin_audit',
}) + '\n');

// Loki's push protocol is Snappy-compressed protobuf. Decode only enough to
// check the real emitted payload without third-party libraries or a Loki server.
function readVarint(buffer, index) {
  let value = 0;
  let shift = 0;
  for (;;) {
    const next = buffer[index++];
    value += (next & 127) * 2 ** shift;
    if (next < 128) return [value, index];
    shift += 7;
  }
}
function unsnappy(buffer) {
  let [size, index] = readVarint(buffer, 0);
  const output = Buffer.alloc(size);
  let offset = 0;
  while (index < buffer.length) {
    const tag = buffer[index++];
    const kind = tag & 3;
    let length;
    let distance;
    if (kind === 0) {
      const encodedLength = tag >> 2;
      if (encodedLength < 60) length = encodedLength + 1;
      else {
        length = 1;
        for (let byte = 0; byte < encodedLength - 59; byte++) length += buffer[index++] * 2 ** (8 * byte);
      }
      buffer.copy(output, offset, index, index + length);
      offset += length;
      index += length;
    } else {
      if (kind === 1) {
        length = 4 + ((tag >> 2) & 7);
        distance = ((tag & 224) << 3) | buffer[index++];
      } else if (kind === 2) {
        length = 1 + (tag >> 2);
        distance = buffer.readUInt16LE(index);
        index += 2;
      } else {
        length = 1 + (tag >> 2);
        distance = buffer.readUInt32LE(index);
        index += 4;
      }
      for (let count = 0; count < length; count++) {
        output[offset] = output[offset - distance];
        offset++;
      }
    }
  }
  return output;
}
function fields(buffer) {
  const result = [];
  for (let index = 0; index < buffer.length;) {
    let tag;
    let value;
    [tag, index] = readVarint(buffer, index);
    const type = tag & 7;
    if (type === 2) {
      let length;
      [length, index] = readVarint(buffer, index);
      value = buffer.subarray(index, index + length);
      index += length;
    } else if (type === 0) [value, index] = readVarint(buffer, index);
    else if (type === 1 || type === 5) {
      const length = type === 1 ? 8 : 4;
      value = buffer.subarray(index, index + length);
      index += length;
    } else throw new Error(`Unexpected protobuf wire type ${type}`);
    result.push({ field: tag >> 3, value });
  }
  return result;
}
const received = [];
const errors = [];
const receiver = http.createServer((request, response) => {
  const chunks = [];
  request.on('data', chunk => chunks.push(chunk));
  request.on('end', () => {
    try {
      for (const stream of fields(unsnappy(Buffer.concat(chunks))).filter(item => item.field === 1)) {
        const streamFields = fields(stream.value);
        const labels = streamFields.find(item => item.field === 1).value.toString();
        for (const entry of streamFields.filter(item => item.field === 2)) {
          const entryFields = fields(entry.value);
          const timestamp = fields(entryFields.find(item => item.field === 1).value);
          received.push({
            labels,
            line: entryFields.find(item => item.field === 2).value.toString(),
            seconds: timestamp.find(item => item.field === 1).value,
          });
        }
      }
      response.writeHead(204);
    } catch (error) {
      errors.push(error.message);
      response.writeHead(500);
    }
    response.end();
  });
});
let alloy;
let output = '';
try {
  await new Promise(resolve => receiver.listen(0, '127.0.0.1', resolve));
  const portReservation = http.createServer();
  await new Promise(resolve => portReservation.listen(0, '127.0.0.1', resolve));
  const alloyPort = portReservation.address().port;
  await new Promise(resolve => portReservation.close(resolve));
  const configuration = fs.readFileSync('monitoring/config.alloy', 'utf8')
    .replaceAll('/app/logs', logDirectory.replaceAll('\\', '/'))
    .replace('http://loki:3100', `http://127.0.0.1:${receiver.address().port}`);
  const configurationPath = path.join(temporary, 'config.alloy');
  fs.writeFileSync(configurationPath, configuration);
  alloy = spawn(executable, ['run', `--server.http.listen-addr=127.0.0.1:${alloyPort}`,
    `--storage.path=${path.join(temporary, 'state')}`, configurationPath], {
    windowsHide: true, env: { ...process.env, TECHNICAL_LOG_RETENTION: '336h' },
  });
  alloy.on('error', error => errors.push(error.message));
  alloy.stderr.on('data', chunk => { output += chunk; });
  alloy.stdout.on('data', () => {});
  await new Promise(resolve => setTimeout(resolve, 9000));
  assert.deepEqual(errors, []);
  assert.equal(received.length, 3, `Expected 3 events; Alloy: ${output.slice(-2000)}`);
  const current = received.find(item => item.labels.includes('source="PipelineTest"'));
  assert(current);
  assert(!current.line.includes('secret123'));
  assert(!current.line.includes('eyJabc'));
  for (const secret of ['secret value with spaces', 'quoted-token', 'single quoted secret', '654321', '987654']) {
    assert(!current.line.includes(secret), `Historical secret leaked: ${secret}`);
  }
  assert(current.line.includes('StatusCode=500'), 'HTTP status must remain visible');
  assert(current.line.includes('Exception: sample\n  at Test.Frame'));
  assert(current.labels.includes('level="info"'));
  const history = received.find(item => item.labels.includes('source_kind="import"'));
  assert(history?.line.includes('historical event'));
  assert.equal(history.seconds, Math.floor(yesterday.getTime() / 1000));
  const audit = received.find(item => item.labels.includes('log_kind="admin_audit"'));
  assert(audit?.line.includes('Test Actor'));
  assert(audit.labels.includes('actorRole="SuperAdmin"'));
  console.log('Alloy fixture passed: multiline, redaction, level, original timestamp, expired/invalid filtering and audit.');
} finally {
  if (alloy) {
    const exited = new Promise(resolve => alloy.once('exit', resolve));
    alloy.kill();
    await Promise.race([exited, new Promise(resolve => setTimeout(resolve, 3000))]);
  }
  await new Promise(resolve => receiver.close(resolve));
  // Verify the resolved absolute cleanup target remains the specific test
  // directory created directly inside the OS temporary directory.
  assert.equal(path.dirname(path.resolve(temporary)), path.resolve(os.tmpdir()));
  assert(path.basename(temporary).startsWith('fc-alloy-check-'));
  fs.rmSync(temporary, { recursive: true, force: true });
}
