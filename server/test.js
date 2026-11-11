// サーバーの動作確認：npm test
// 部屋を作る → 入る → データをやりとりする → 抜ける、の流れを確かめる。
'use strict';
const assert = require('assert');
const WebSocket = require('ws');
const { server } = require('./server');

function open(port) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(`ws://127.0.0.1:${port}/ws`);
    ws.inbox = [];
    ws.waiters = [];
    ws.on('message', (data, isBinary) => {
      const m = isBinary ? { bin: Buffer.from(data) } : JSON.parse(data.toString());
      const w = ws.waiters.shift();
      if (w) w(m); else ws.inbox.push(m);
    });
    ws.on('open', () => resolve(ws));
    ws.on('error', reject);
  });
}
function next(ws, ms = 2000) {
  if (ws.inbox.length) return Promise.resolve(ws.inbox.shift());
  return new Promise((resolve, reject) => {
    const t = setTimeout(() => reject(new Error('返事が来ない')), ms);
    ws.waiters.push((m) => { clearTimeout(t); resolve(m); });
  });
}
const send = (ws, o) => ws.send(JSON.stringify(o));
const frame = (id, bytes) => { const b = Buffer.alloc(4 + bytes.length); b.writeUInt32LE(id, 0); Buffer.from(bytes).copy(b, 4); return b; };

(async () => {
  await new Promise((r) => server.listen(0, r));
  const port = server.address().port;

  const host = await open(port);
  send(host, { t: 'create' });
  const created = await next(host);
  assert.strictEqual(created.t, 'created');
  assert.match(created.code, /^\d{4}$/);
  console.log('部屋を作れた：', created.code);

  const bad = await open(port);
  send(bad, { t: 'join', code: '0000' });
  assert.strictEqual((await next(bad)).t, 'error');
  console.log('間違った番号は断られた');

  const a = await open(port);
  send(a, { t: 'join', code: created.code });
  const joined = await next(a);
  assert.deepStrictEqual(joined, { t: 'joined', id: 1 });
  assert.deepStrictEqual(await next(host), { t: 'peer', id: 1 });
  console.log('部屋に入れた');

  a.send(Buffer.from([1, 2, 3]));
  const toHost = await next(host);
  assert.deepStrictEqual([...toHost.bin], [1, 0, 0, 0, 1, 2, 3]);
  host.send(frame(1, [9, 8]));
  assert.deepStrictEqual([...(await next(a)).bin], [9, 8]);
  console.log('データを中継できた');

  // 満員：ホスト＋5人まで
  const others = [];
  for (let i = 0; i < 4; i++) { const w = await open(port); send(w, { t: 'join', code: created.code }); assert.strictEqual((await next(w)).t, 'joined'); await next(host); others.push(w); }
  const full = await open(port);
  send(full, { t: 'join', code: created.code });
  assert.strictEqual((await next(full)).t, 'error');
  console.log('7人目は満員で断られた');

  a.close();
  assert.deepStrictEqual(await next(host), { t: 'left', id: 1 });
  console.log('抜けたことがホストに伝わった');

  host.close();
  assert.strictEqual((await next(others[0])).t, 'closed');
  console.log('ホストが抜けると部屋が閉じた');

  for (const w of [bad, full, ...others]) w.close();
  server.close();
  console.log('すべて OK');
  process.exit(0);
})().catch((e) => { console.error('失敗：', e); process.exit(1); });
