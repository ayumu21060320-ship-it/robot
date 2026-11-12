// Web 版のマルチ：ブラウザの WebRTC を PeerJS（https://peerjs.com）で使い、ブラウザどうしを直接つなぐ。
//
// - 部屋を作る人（ホスト）は、PeerJS の無料の仲介サーバーに「titancrew-4821」のような名前で登録する
// - 入る人は、その名前（部屋番号）を探してホストへ直接つなぐ。データはホストと入る人のあいだを直接流れる
//   （直接つながらない回線では、PeerJS の無料の中継サーバー TURN を通る）
//
// C#（PeerTransport.cs）からは、毎フレーム PJB_Poll で届いたできごとを1つずつ取りに来る。
// PeerJS 本体は、ゲームと同じ場所の peerjs.min.js（Robot → Web版をビルド でコピーされる）か、なければ unpkg から読み込む。
mergeInto(LibraryManager.library, {
  $PJB: {
    state: 0,          // 0 = 準備中, 1 = 使える（ホストは部屋番号が決まった／入る人はつながった）, 2 = 失敗・切断
    error: '',
    code: '',
    peer: null,
    isHost: false,
    conns: {},         // ホスト：番号 → DataConnection
    connIds: null,     // ホスト：DataConnection → 番号
    nextId: 1,
    conn: null,        // 入る人：ホストへの DataConnection
    events: [],        // { type: 0 データ / 1 つながった / 2 切れた / 3 失敗, id, data }
    prefix: 'titancrew-',
    tries: 0,

    load: function (done) {
      if (window.Peer) { done(); return; }
      var urls = ['peerjs.min.js', 'https://unpkg.com/peerjs@1.5.5/dist/peerjs.min.js', 'https://cdn.jsdelivr.net/npm/peerjs@1.5.5/dist/peerjs.min.js'];
      var i = 0;
      var next = function () {
        if (window.Peer) { done(); return; }
        if (i >= urls.length) { PJB.fail('通信の部品（PeerJS）を読み込めませんでした。インターネットにつながっているか確かめてください。'); return; }
        var s = document.createElement('script');
        s.src = urls[i++];
        s.onload = function () { window.Peer ? done() : next(); };
        s.onerror = next;
        document.head.appendChild(s);
      };
      next();
    },

    // PeerJS の設定。ふだんは PeerJS の無料の仲介サーバーを使う。
    // 自分の仲介サーバーを使うときや試験のときは、ページで window.TITAN_PEER_OPTIONS = { host, port, path, secure } を決めておく
    options: function () {
      var o = { debug: 0 };
      var extra = window.TITAN_PEER_OPTIONS || {};
      for (var k in extra) o[k] = extra[k];
      return o;
    },

    fail: function (msg) {
      if (PJB.state === 2) return;
      PJB.error = msg;
      PJB.state = 2;
      PJB.events.push({ type: 3, id: 0, data: null });
    },

    explain: function (err) {
      var t = err && err.type;
      if (t === 'peer-unavailable') return 'その部屋番号の部屋が見つかりません。番号を確かめてください（部屋を作った人が抜けると部屋は消えます）。';
      if (t === 'network' || t === 'server-error' || t === 'socket-error' || t === 'socket-closed')
        return '仲介サーバー（PeerJS）につながりませんでした。回線を確かめてください（学校のネットワークでは遮断されることがあります）。';
      if (t === 'browser-incompatible') return 'このブラウザはマルチ（WebRTC）に対応していません。Chrome か Edge で開いてください。';
      return '通信のエラー：' + (t || '') + ' ' + (err && err.message ? err.message : '');
    },

    reset: function () {
      PJB.close();
      PJB.state = 0; PJB.error = ''; PJB.code = '';
      PJB.conns = {}; PJB.connIds = new Map(); PJB.nextId = 1; PJB.conn = null;
      PJB.events = []; PJB.tries = 0;
    },

    // ---- ホスト：4けたの番号で登録する。使われていたら別の番号で登録し直す
    host: function () {
      PJB.reset();
      PJB.isHost = true;
      PJB.load(PJB.tryHost);
    },

    tryHost: function () {
      if (PJB.tries++ > 8) { PJB.fail('空いている部屋番号が見つかりませんでした。もう一度試してください。'); return; }
      var code = String(1000 + Math.floor(Math.random() * 9000));
      var peer = new window.Peer(PJB.prefix + code, PJB.options());
      PJB.peer = peer;
      peer.on('open', function () { PJB.code = code; PJB.state = 1; });
      peer.on('connection', function (c) {
        var id = PJB.nextId++;
        PJB.connIds.set(c, id);
        c.on('open', function () {
          PJB.conns[id] = c;
          PJB.events.push({ type: 1, id: id, data: null });
        });
        c.on('data', function (d) { PJB.events.push({ type: 0, id: id, data: PJB.bytes(d) }); });
        c.on('close', function () {
          if (PJB.conns[id] !== c) return;
          delete PJB.conns[id];
          PJB.events.push({ type: 2, id: id, data: null });
        });
        c.on('error', function () { try { c.close(); } catch (e) {} });
      });
      peer.on('error', function (err) {
        if (err.type === 'unavailable-id' && PJB.state === 0) { peer.destroy(); PJB.tryHost(); return; }
        if (PJB.state === 1 && (err.type === 'peer-unavailable' || err.type === 'webrtc')) return;   // 個別のつなぎ損ね
        PJB.fail(PJB.explain(err));
      });
      peer.on('disconnected', function () {
        // 仲介サーバーとの接続だけ切れた（つながっている人とは続けられる）。新しい人が入れるように、つなぎ直す
        if (PJB.state === 1 && !peer.destroyed) { try { peer.reconnect(); } catch (e) {} }
      });
    },

    // ---- 入る人：部屋番号のホストへつなぐ
    join: function (code) {
      PJB.reset();
      PJB.isHost = false;
      PJB.code = code;
      PJB.load(function () {
        var peer = new window.Peer(PJB.options());
        PJB.peer = peer;
        peer.on('open', function () {
          var c = peer.connect(PJB.prefix + code, { reliable: true });   // 大きなデータは PeerJS が分けて送る
          PJB.conn = c;
          var timer = setTimeout(function () { if (PJB.state === 0) PJB.fail('部屋を作った人につながりませんでした（時間切れ）。番号を確かめて、もう一度試してください。'); }, 15000);
          c.on('open', function () {
            clearTimeout(timer);
            PJB.state = 1;
            PJB.events.push({ type: 1, id: 0, data: null });
            // ホストとつながったら、仲介サーバーとはもう話さなくてよい
            try { peer.disconnect(); } catch (e) {}
          });
          c.on('data', function (d) { PJB.events.push({ type: 0, id: 0, data: PJB.bytes(d) }); });
          c.on('close', function () {
            if (PJB.state === 2) return;
            PJB.error = PJB.error || '部屋から切断されました（部屋を作った人が抜けると部屋はなくなります）。';
            PJB.state = 2;
            PJB.events.push({ type: 2, id: 0, data: null });
          });
          c.on('error', function () { try { c.close(); } catch (e) {} });
        });
        peer.on('error', function (err) {
          if (PJB.state === 1 && err.type !== 'webrtc') return;   // つながった後の仲介サーバーのエラーは気にしない
          PJB.fail(PJB.explain(err));
        });
      });
    },

    bytes: function (d) {
      if (d instanceof ArrayBuffer) return new Uint8Array(d);
      if (ArrayBuffer.isView(d)) return new Uint8Array(d.buffer, d.byteOffset, d.byteLength);
      if (typeof d === 'string') return new TextEncoder().encode(d);
      return new Uint8Array(0);
    },

    send: function (to, bytes) {
      var c = PJB.isHost ? PJB.conns[to] : PJB.conn;
      if (c && c.open) c.send(bytes);
    },

    kick: function (id) {
      var c = PJB.conns[id];
      if (!c) return;
      delete PJB.conns[id];
      try { c.close(); } catch (e) {}
    },

    close: function () {
      try { if (PJB.conn) PJB.conn.close(); } catch (e) {}
      for (var k in PJB.conns) { try { PJB.conns[k].close(); } catch (e) {} }
      try { if (PJB.peer) PJB.peer.destroy(); } catch (e) {}
      PJB.peer = null; PJB.conn = null; PJB.conns = {};
    },

    str: function (s) {
      var size = lengthBytesUTF8(s) + 1;
      var buf = _malloc(size);
      stringToUTF8(s, buf, size);
      return buf;
    },
  },

  PJB_Host__deps: ['$PJB'],
  PJB_Host: function () { PJB.host(); },

  PJB_Join__deps: ['$PJB'],
  PJB_Join: function (codePtr) { PJB.join(UTF8ToString(codePtr)); },

  PJB_State__deps: ['$PJB'],
  PJB_State: function () { return PJB.state; },

  PJB_Code__deps: ['$PJB'],
  PJB_Code: function () { return PJB.str(PJB.code); },

  PJB_Error__deps: ['$PJB'],
  PJB_Error: function () { return PJB.str(PJB.error); },

  // 次のできごとの種類（なければ -1）。番号と長さは PJB_PeekId / PJB_PeekLength
  PJB_PeekType__deps: ['$PJB'],
  PJB_PeekType: function () { return PJB.events.length ? PJB.events[0].type : -1; },

  PJB_PeekId__deps: ['$PJB'],
  PJB_PeekId: function () { return PJB.events.length ? PJB.events[0].id : 0; },

  PJB_PeekLength__deps: ['$PJB'],
  PJB_PeekLength: function () { var e = PJB.events[0]; return e && e.data ? e.data.length : 0; },

  // 次のできごとを取り出す（データなら ptr に写す）
  PJB_Pop__deps: ['$PJB'],
  PJB_Pop: function (ptr, len) {
    var e = PJB.events.shift();
    if (e && e.data && len > 0) HEAPU8.set(e.data.subarray(0, len), ptr);
  },

  PJB_Send__deps: ['$PJB'],
  PJB_Send: function (to, ptr, len) { PJB.send(to, HEAPU8.slice(ptr, ptr + len)); },

  PJB_Kick__deps: ['$PJB'],
  PJB_Kick: function (id) { PJB.kick(id); },

  PJB_Close__deps: ['$PJB'],
  PJB_Close: function () { PJB.close(); PJB.state = 0; PJB.events = []; },
});
