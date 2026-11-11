// タイタンクルーのサーバー。
//
// 1. Web 版のゲーム（public/ に置いたファイル）を配る
// 2. 部屋番号ごとに、ホスト（部屋を作った人）とほかの人の通信を WebSocket で中継する
//
// ゲームの計算はすべてホストのブラウザ（または PC）が行い、このサーバーは中身を見ずに転送するだけ。
//
//   npm install
//   npm start              # http://localhost:8080 で遊べる（PORT 環境変数で変えられる）
//
// ---- 通信のきまり（/ws）
// 文字のメッセージ（JSON）は部屋の操作、バイナリのメッセージはゲームのデータ。
//   部屋を作る：  → {"t":"create"}           ← {"t":"created","code":"1234"}
//   部屋に入る：  → {"t":"join","code":"1234"} ← {"t":"joined","id":3}  または {"t":"error","msg":"..."}
//   ホストへ：    ← {"t":"peer","id":3}（だれかが入った）  ← {"t":"left","id":3}（抜けた）
//   ホストから：  → {"t":"kick","id":3}（その人を外す）
//   入った人へ：  ← {"t":"closed","msg":"..."}（ホストが抜けて部屋がなくなった）
//   データ：ホスト → サーバー は [宛先の番号 4バイト][中身]、サーバー → 入った人 は [中身] だけ
//           入った人 → サーバー は [中身] だけ、サーバー → ホスト は [送った人の番号 4バイト][中身]
'use strict';

const http = require('http');
const fs = require('fs');
const path = require('path');
const { WebSocketServer } = require('ws');

const PORT = Number(process.env.PORT) || 8080;
// 配るゲームのフォルダ：PUBLIC_DIR → server/public → Unity の書き出し先（robot/Builds/Web）の順に探す
const PUBLIC = process.env.PUBLIC_DIR ? path.resolve(process.env.PUBLIC_DIR)
  : fs.existsSync(path.join(__dirname, 'public')) ? path.join(__dirname, 'public')
  : path.join(__dirname, '..', 'robot', 'Builds', 'Web');
const MAX_PLAYERS = 6;               // ホストを含めた1部屋の最大人数
const MAX_MESSAGE = 1024 * 1024;     // 1回に送れるデータの上限（1MB）

// ------------------------------------------------------------------ ファイルを配る

const TYPES = {
  '.html': 'text/html; charset=utf-8', '.js': 'application/javascript', '.css': 'text/css',
  '.wasm': 'application/wasm', '.data': 'application/octet-stream', '.json': 'application/json',
  '.png': 'image/png', '.ico': 'image/x-icon', '.unityweb': 'application/octet-stream',
};

function serveFile(req, res) {
  let url = decodeURIComponent((req.url || '/').split('?')[0]);
  if (url.endsWith('/')) url += 'index.html';
  const file = path.normalize(path.join(PUBLIC, url));
  if (!file.startsWith(PUBLIC)) { res.writeHead(403); res.end(); return; }
  fs.stat(file, (err, st) => {
    if (err || !st.isFile()) {
      res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
      res.end(fs.existsSync(path.join(PUBLIC, 'index.html'))
        ? 'Not found'
        : 'ゲームのファイルがまだありません。Unity でメニュー「Robot → Web版をビルド」を実行してください（robot/Builds/Web に書き出されます）。');
      return;
    }
    const headers = { 'Content-Length': st.size, 'Cache-Control': 'no-cache' };
    let ext = path.extname(file);
    // Unity の gzip 圧縮ファイル（*.js.gz など）は、ブラウザに展開してもらう
    if (ext === '.gz') { headers['Content-Encoding'] = 'gzip'; ext = path.extname(file.slice(0, -3)); }
    if (ext === '.br') { headers['Content-Encoding'] = 'br'; ext = path.extname(file.slice(0, -3)); }
    headers['Content-Type'] = TYPES[ext] || 'application/octet-stream';
    res.writeHead(200, headers);
    fs.createReadStream(file).pipe(res);
  });
}

const server = http.createServer((req, res) => {
  if (req.url === '/health') { res.writeHead(200, { 'Content-Type': 'text/plain' }); res.end(`ok rooms=${rooms.size}`); return; }
  serveFile(req, res);
});

// ------------------------------------------------------------------ 部屋

/** code → { host, peers: Map<id, ws>, nextId } */
const rooms = new Map();

function newCode() {
  for (let i = 0; i < 1000; i++) {
    const code = String(1000 + Math.floor(Math.random() * 9000));   // 4けたの部屋番号
    if (!rooms.has(code)) return code;
  }
  return null;
}

function sendJson(ws, obj) {
  if (ws.readyState === ws.OPEN) ws.send(JSON.stringify(obj));
}

function closeRoom(room, msg) {
  rooms.delete(room.code);
  for (const peer of room.peers.values()) {
    sendJson(peer, { t: 'closed', msg });
    peer.close(1000, 'room closed');
  }
  room.peers.clear();
  log(`部屋 ${room.code} を閉じた（${msg}）`);
}

function log(s) { console.log(new Date().toISOString().slice(11, 19), s); }

const wss = new WebSocketServer({ server, path: '/ws', maxPayload: MAX_MESSAGE });

wss.on('connection', (ws) => {
  ws.isAlive = true;
  ws.on('pong', () => { ws.isAlive = true; });
  // ws.room：入っている部屋、ws.peerId：0 ならホスト、1 以上なら入った人の番号

  ws.on('message', (data, isBinary) => {
    const room = ws.room;
    if (!isBinary) {
      let m;
      try { m = JSON.parse(data.toString()); } catch { return; }
      if (m.t === 'create' && !room) {
        const code = newCode();
        if (!code) { sendJson(ws, { t: 'error', msg: '部屋がいっぱいです。しばらくしてから試してください。' }); return; }
        const r = { code, host: ws, peers: new Map(), nextId: 1 };
        rooms.set(code, r);
        ws.room = r; ws.peerId = 0;
        sendJson(ws, { t: 'created', code });
        log(`部屋 ${code} を作った`);
      } else if (m.t === 'join' && !room) {
        const r = rooms.get(String(m.code || '').trim());
        if (!r) { sendJson(ws, { t: 'error', msg: 'その部屋番号の部屋が見つかりません。番号を確かめてください（部屋を作った人が抜けると部屋は消えます）。' }); return; }
        if (r.peers.size + 1 >= MAX_PLAYERS) { sendJson(ws, { t: 'error', msg: `満員です（最大${MAX_PLAYERS}人）。` }); return; }
        const id = r.nextId++;
        r.peers.set(id, ws);
        ws.room = r; ws.peerId = id;
        sendJson(ws, { t: 'joined', id });
        sendJson(r.host, { t: 'peer', id });
        log(`部屋 ${r.code} に ${id} 番が入った（${r.peers.size + 1}人）`);
      } else if (m.t === 'kick' && room && ws.peerId === 0) {
        const peer = room.peers.get(m.id);
        if (peer) { room.peers.delete(m.id); peer.close(1000, 'kicked'); }
      }
      return;
    }

    // ---- ゲームのデータ
    if (!room) return;
    const buf = Buffer.isBuffer(data) ? data : Buffer.concat(data);
    if (ws.peerId === 0) {
      if (buf.length < 4) return;
      const to = room.peers.get(buf.readUInt32LE(0));
      if (to && to.readyState === to.OPEN) to.send(buf.subarray(4), { binary: true });
    } else {
      const host = room.host;
      if (host.readyState !== host.OPEN) return;
      const out = Buffer.allocUnsafe(buf.length + 4);
      out.writeUInt32LE(ws.peerId, 0);
      buf.copy(out, 4);
      host.send(out, { binary: true });
    }
  });

  ws.on('close', () => {
    const room = ws.room;
    if (!room) return;
    if (ws.peerId === 0) {
      if (rooms.get(room.code) === room) closeRoom(room, '部屋を作った人が抜けました。');
    } else if (room.peers.get(ws.peerId) === ws) {
      room.peers.delete(ws.peerId);
      sendJson(room.host, { t: 'left', id: ws.peerId });
      log(`部屋 ${room.code} から ${ws.peerId} 番が抜けた`);
    }
  });
});

// 何もしないと途中のサーバー（ホスティング側）が通信を切ることがあるので、25秒ごとに生存確認
const heartbeat = setInterval(() => {
  for (const ws of wss.clients) {
    if (!ws.isAlive) { ws.terminate(); continue; }
    ws.isAlive = false;
    ws.ping();
  }
}, 25000);
wss.on('close', () => clearInterval(heartbeat));

if (require.main === module) {
  server.listen(PORT, () => log(`タイタンクルーのサーバーを起動しました：http://localhost:${PORT}`));
}

module.exports = { server, rooms };
