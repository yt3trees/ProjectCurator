# WPF フラッシュ問題 - 試したこと記録

対象: ホットキー/トレイアイコンでウィンドウを Show() するとき、起動時に一瞬青または白くフラッシュする問題。

環境: WPF (.NET 9) + wpf-ui FluentWindow, ExtendsContentIntoTitleBar=True, ShowInTaskbar=False

ステータス: 解決済み (2026-03-21)

---

## 根本原因

`Hide()`/`Show()` のたびに DWM がウィンドウサーフェスを再初期化する。
WPF がコンテンツを描画するまでの間 (数フレーム)、DWM はデフォルトの青/白背景を表示する。
この「空サーフェス + デフォルト色」が一瞬見えるのがフラッシュの正体。

---

## 失敗した試み

### 1. WindowBackdropType="None" のみ

変更: FluentWindow に `WindowBackdropType="None"` を追加
結果: 改善なし。Mica 無効化だけではフラッシュは消えなかった。

---

### 2. Hide()/Show() を WindowState.Minimized に変更

変更: `ToggleVisibility()` と `OnClosing()` で `Hide()` の代わりに `WindowState = WindowState.Minimized` を使用
結果: ShowInTaskbar="False" + Minimized の組み合わせで画面左下に小窓が残る問題が発生。フラッシュも残った。

---

### 3. SetLayeredWindowAttributes(alpha=0) → 復元方式

変更:
- Hide() 前に alpha=0 にして「透明化」
- Show() + Activate() 後に Task.Delay(300ms) を挟んで alpha=255 に復元

結果: まったく変化なし。
原因判明: WPF は alpha=0 のウィンドウに対してレンダリングを一時停止する。
alpha を255に戻した瞬間に D3D サーフェスが空(白/青デフォルト)のまま表示される。

---

### 4. Window.Opacity=0 → 復元 + CompositionTarget.Rendering フレームカウント方式

変更:
- Hide() 前に Opacity=0
- Show() 後に CompositionTarget.Rendering で2フレーム待機して Opacity=1

結果: まだフラッシュが出る場合がある。
原因: CompositionTarget.Rendering は描画「直前」に発火するため、
フレームカウントしても実際に描画が完了したタイミングと同期できない。

---

### 5. FlashBlocker (WPF Grid オーバーレイ) + Window.Opacity

変更:
- XAML に AppBackground 色の全画面 Grid (FlashBlocker, ZIndex=1000) を追加
- Hide() 前: FlashBlocker を Visible にして1フレーム待機後 Hide()
- Show() 後: Show() + Activate() + Focus() → 次フレームで FlashBlocker を Collapsed
- 起動時: App.xaml.cs で Opacity=0 → OnLoaded で Opacity=1 + CollapseFlashBlockerNextFrame()

追加した Win32 補助:
- WM_ERASEBKGND フックで背景消去を無効化
- SetClassLongPtr(GCLP_HBRBACKGROUND, 0) でクラス背景ブラシを除去
- DWMWA_TRANSITIONS_FORCEDISABLED=1 で DWM 遷移アニメーション無効化
- source.CompositionTarget.BackgroundColor を暗い色 (#0d1117) に設定

結果: ときどき改善されるが、まだ青/白でフラッシュすることがある。
特に起動時とホットキーで Show() する瞬間に再現。

理由: CompositionTarget.Rendering は GPU への描画サブミット「直前」に発火するため、
Hide() を呼んだ時点では FlashBlocker フレームがまだ DWM キャッシュに届いていない場合がある。

---

## 解決策: 画面外移動方式 (ステータス: 成功)

### アイデアの核心

Hide()/Show() を一切やめる。ウィンドウは一度 Show() したら以後ずっと表示状態を維持する。
「隠す」ときは座標 (-32000, -32000) に移動するだけ。
DWM はサーフェスを再初期化しないため、フラッシュが起きる仕組み自体がなくなる。

### 変更内容

App.xaml.cs:
- Show() 前に `mainWindow.Left = -32000; mainWindow.Top = -32000;` を設定
- これで起動時は画面外で Show() → DWM が画面外で初期化 → フラッシュがユーザーに見えない

MainWindow.xaml.cs:
- `_isShownOnScreen` フラグで画面上/画面外の状態を管理
- `MoveOffScreen()`: FlashBlocker を Visible にして1フレーム後に Left/Top = -32000 に移動
- `MoveOnScreen()`: WorkArea 中央に Left/Top を設定 → Activate/Focus → 次フレームで FlashBlocker Collapsed
- `ToggleVisibility()`: `_isShownOnScreen && IsActive` で分岐
- `OnClosing()` / `OnKeyDown(Escape)`: MoveOffScreen() を呼ぶ
- `OnLoaded()`: MoveOnScreen() を呼んで初回表示

FlashBlocker (XAML Grid, ZIndex=1000) は維持:
- 画面外から画面内に移動する瞬間に WPF がコンテンツを描画するまでの間を覆うため

### 残した Win32 補助 (副作用として有効)

- WM_ERASEBKGND フック
- SetClassLongPtr(GCLP_HBRBACKGROUND, 0)
- DWMWA_TRANSITIONS_FORCEDISABLED=1
- source.CompositionTarget.BackgroundColor = #0d1117
