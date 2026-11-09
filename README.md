# タイタンクルー（robot）

巨大ロボットの**中**にいる操縦士たちが、ロボット内部を走り回り、脚・腕・頭部砲・動力炉の操作盤に乗り込んで、1体のロボットを協力して動かすオンライン協力ゲーム。

- 画面の左半分：ロボット内部を真上から見た地図。自分の操縦士を追いかける
- 画面の右半分：外を歩く巨大ロボット（3D）

Unity プロジェクト本体は `robot/` フォルダにあります。

## 動かし方

1. Unity Hub で `robot/` フォルダを開く（Unity 6000.4）
   - 初回は Netcode for GameObjects（`com.unity.netcode.gameobjects` 2.9.2）が自動で入ります
2. メニュー **Robot → ゲームのシーンを作成** を押す
   - `Assets/Game/Scenes/RobotGame.unity` ができて、開いた状態になります
3. ▶ 再生して **ホストとして始める** を押す

### Netcode が入らない場所では（オフライン版）

学校のネットワークなどで `packages.unity.com` につながらず Netcode が入らないときも、そのまま開けます。
Netcode の代わりに `Assets/Scripts/Offline/` の代役が使われ、メニューに「1人で始める」が出るオフライン版（1人用）になります。

- Package Manager に Netcode のエラーが出ますが、ゲームは動きます
- インターネットにつながる場所で開くと Netcode が自動で入り、オンライン版に切り替わります。
  そのあと **Robot → ゲームのシーンを作成** をもう一度押してください（シーンとプレハブを作り直すため）

### 2人以上で試す

- **同じPCで**：File → Build Profiles でビルドして起動し、片方で「ホストとして始める」、もう片方は アドレス `127.0.0.1` のまま「参加する」
- **同じWi-Fi/LANで**：ホスト側のメニューに出る「このPCのアドレス」（例 `192.168.1.10`）を参加する人が入力
- **インターネット越し**：招待コードで参加できる Relay 方式を次の段階で追加予定

## 操作

| キー | 操縦席にいないとき | 操縦席にいるとき |
|---|---|---|
| WASD / 矢印 | 内部を歩く | 席ごとの操作（下の表） |
| E | 近くの席に乗る | 席から降りる |
| Space | 火災の消火（連打） | アクション |
| Tab（押している間） | 内部の全体図 | 内部の全体図 |

ゲームパッド：左スティックで移動、A でアクション、B で乗る／降りる。

| 席 | 操作 |
|---|---|
| 左脚・右脚 | W/S でレバー。両脚をそろえると前進、差をつけると曲がる。Space で停止。レバーは降りても残る |
| 左腕・右腕 | A/D で狙い、Space でパンチ（敵の巣に大ダメージ） |
| 頭部砲 | A/D で砲塔を回し、Space 長押しで連射 |
| 動力炉 | Space 連打で発電。動力が 0 になるとロボットが止まる |

被弾すると、どこかの席で火災が起きて使えなくなります。その席まで走って Space 連打で消火。

## コードの構成

| ファイル | 役割 |
|---|---|
| `Assets/Scripts/Core/GameBootstrap.cs` | 全体の入口。左右2台のカメラ、接続メニュー、画面表示 |
| `Assets/Scripts/Core/Stations.cs` | 6つの操縦席の定義（名前・位置・種類） |
| `Assets/Scripts/Map/InteriorMap.cs` | ロボット内部の部屋と通路、歩ける場所の判定、内部の見た目 |
| `Assets/Scripts/Map/ExteriorWorld.cs` | 外の世界（地面・ビル・敵の巣） |
| `Assets/Scripts/Net/RobotState.cs` | ロボットの状態とゲームの判定（ホストが計算して全員に配る） |
| `Assets/Scripts/Net/CrewPlayer.cs` | 操縦士1人ぶん（移動・入力・席の乗り降り） |
| `Assets/Scripts/Visual/RobotVisual.cs` | 外のロボット・ドローン・爆発などの見た目 |
| `Assets/Scripts/Editor/RobotGameSetup.cs` | 「Robot → ゲームのシーンを作成」メニュー |
| `Assets/Scripts/Offline/` | Netcode が入っていないときだけ使うオフライン用の代役 |
| `Assets/Scripts/Game.asmdef` | Netcode の有無で `HAS_NETCODE` を切り替える |

見た目はまだキューブや円柱の仮組みです。モデルができたら `RobotVisual.Build` と `InteriorMap.Build` を差し替えます。
