// Web 版で、ブラウザの WebSocket を C#（WebSocketTransport.cs）から使うための橋渡し。
// 受け取ったメッセージはここで順番にためておき、C# 側が毎フレーム取りに来る。
mergeInto(LibraryManager.library, {
  $WSB: { next: 1, socks: {} },

  WSB_Open__deps: ['$WSB'],
  WSB_Open: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var id = WSB.next++;
    var s = { ws: null, q: [], state: 0, err: '' };   // state: 0 = つなぎ中, 1 = つながった, 2 = 切れた
    WSB.socks[id] = s;
    try { s.ws = new WebSocket(url); }
    catch (e) { s.state = 2; s.err = '' + e; return id; }
    s.ws.binaryType = 'arraybuffer';
    s.ws.onopen = function () { s.state = 1; };
    s.ws.onmessage = function (ev) {
      if (typeof ev.data === 'string') s.q.push({ text: 1, data: new TextEncoder().encode(ev.data) });
      else s.q.push({ text: 0, data: new Uint8Array(ev.data) });
    };
    s.ws.onerror = function () { if (!s.err) s.err = 'サーバーにつながりませんでした'; };
    s.ws.onclose = function (ev) { s.state = 2; if (!s.err) s.err = '切断されました（' + ev.code + '）'; };
    return id;
  },

  WSB_State__deps: ['$WSB'],
  WSB_State: function (id) { var s = WSB.socks[id]; return s ? s.state : 2; },

  WSB_NextLength__deps: ['$WSB'],
  WSB_NextLength: function (id) { var s = WSB.socks[id]; return s && s.q.length ? s.q[0].data.length : -1; },

  WSB_NextIsText__deps: ['$WSB'],
  WSB_NextIsText: function (id) { var s = WSB.socks[id]; return s && s.q.length ? s.q[0].text : 0; },

  WSB_Recv__deps: ['$WSB'],
  WSB_Recv: function (id, ptr, len) {
    var s = WSB.socks[id];
    if (!s || !s.q.length) return;
    var m = s.q.shift();
    HEAPU8.set(m.data.subarray(0, len), ptr);
  },

  WSB_Send__deps: ['$WSB'],
  WSB_Send: function (id, ptr, len) {
    var s = WSB.socks[id];
    if (s && s.state === 1) s.ws.send(HEAPU8.slice(ptr, ptr + len));
  },

  WSB_SendText__deps: ['$WSB'],
  WSB_SendText: function (id, strPtr) {
    var s = WSB.socks[id];
    if (s && s.state === 1) s.ws.send(UTF8ToString(strPtr));
  },

  WSB_Close__deps: ['$WSB'],
  WSB_Close: function (id) {
    var s = WSB.socks[id];
    if (!s) return;
    try { if (s.ws) s.ws.close(); } catch (e) {}
    delete WSB.socks[id];
  },

  WSB_Error__deps: ['$WSB'],
  WSB_Error: function (id) {
    var s = WSB.socks[id];
    var str = s ? s.err : '';
    var size = lengthBytesUTF8(str) + 1;
    var buf = _malloc(size);
    stringToUTF8(str, buf, size);
    return buf;
  },

  // ゲームを開いているページと同じサーバーの /ws（https なら wss）
  WSB_PageUrl: function () {
    var loc = window.location;
    var str = (loc.protocol === 'http:' || loc.protocol === 'https:')
      ? (loc.protocol === 'https:' ? 'wss://' : 'ws://') + loc.host + '/ws' : '';
    var size = lengthBytesUTF8(str) + 1;
    var buf = _malloc(size);
    stringToUTF8(str, buf, size);
    return buf;
  },
});
