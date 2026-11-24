# tasoller-plus-dotnet

Tasoller Plus の入出力を取り扱う C# ライブラリです。\
対応機器の検出と接続、スライダ・AIR センサ・FN ボタンの入力取得、LED の発光指定を提供します。

## 対応機器

対応機器は **Tasoller Plus** です。無印の **Tasoller** は非対応です。\
Tasoller Plus は **WinUSB モード** で動作している必要があります。モードの切り替え方は[公式取扱説明書](https://www.dj-dao.com/DOWNLOAD/tasollerplus/TASOLLER%20PLUS_GUIDE_JP_V1.0_Web.pdf)を参照してください。

対象フレームワークは `.NET Standard 2.1` です。\
Windows x64（.NET 10.0.12）で、Tasoller Plus 実機による動作を確認しています。

## インストール

NuGet.org で公開しています。 [TasollerPlus のパッケージページ](https://www.nuget.org/packages/TasollerPlus)

追加先の `.csproj` があるディレクトリで、次のコマンドを実行します。

```powershell
dotnet add package TasollerPlus --version 1.0.0
```

## 使い方

代表的な使用方法を記載します。詳細な仕様は [API リファレンス](docs/api/TasollerPlus.md) を参照してください。

### デバイスの作成

`TasollerPlusDevice` を作成します。1インスタンスで1台の通信を扱います。作成直後の通信状態は `Disconnected` です。

```csharp
var device = new TasollerPlusDevice();
```

### 接続デバイスの検出

`DetectDevice()` は、機器との通信を開始せず、呼び出し時点で Windows が認識している対応機器を検索します。

```csharp
var detection = device.DetectDevice();
Console.WriteLine($"機器検出結果 {detection}"); // DeviceFound, DeviceNotFound, MultipleDevicesFound, DetectionFailed
```

### 接続

`Connect()` は、機器を検索し、対応機器が1台なら接続を試みます。

```csharp
var connection = device.Connect();
Console.WriteLine($"接続結果 {connection}"); // Success, AlreadyConnected, DeviceNotFound, MultipleDevicesFound, ConnectionFailed
```

### 未読の入力を受信順に取得

`ReadPendingInputs()` は、まだ読み取っていない入力を、受信した順に指定された配列へコピーします。

配列にコピーされた件数は `Count` から取得できます。配列の先頭 `Count` 件にコピーされたデータが入ります。\
コピーされた入力は未読分から取り除かれ、配列に入りきらなかった入力は未読のまま残ります。

以下の例では、ライブラリが保持できる未読入力の最大件数（`InputBufferCapacity`）を配列の長さにしています。

```csharp
const int FirstSliderIndex = 0;

var pendingInputs = new TasollerPlusInputFrame[device.InputBufferCapacity];
var readResult = device.ReadPendingInputs(pendingInputs);
for (var index = 0; index < readResult.Count; index++)
{
    var pendingFrame = pendingInputs[index];
    Console.WriteLine($"受信連番 {pendingFrame.SequenceNumber}、スライダ先頭 {pendingFrame.GetSliderValue(FirstSliderIndex)}"); // 連番は1から増加、スライダ値は0〜255
}

if (readResult.DroppedCount > 0)
{
    Console.WriteLine($"キューの上限を超えて破棄された入力 {readResult.DroppedCount} 件");
}
```

未読入力がライブラリの上限を超えた場合、古い入力から破棄されます。前回の読み取り以降に破棄された件数は `DroppedCount` から取得できます。

### 最新の入力を取得

`TryGetLatestInput()` は、最後に受信した入力を取得できます。接続直後は、最初の入力が届くまで `false` を返します。\
最新フレームは、未読入力キューを消費せず繰り返し取得できます。

```csharp
const int FirstSliderIndex = 0;
const int BottomAirSensorIndex = 0;

if (device.TryGetLatestInput(out var frame))
{
    Console.WriteLine($"スライダ {frame.GetSliderValue(FirstSliderIndex)}"); // 0〜255
    Console.WriteLine($"最下段の AIR 遮断 {frame.IsAirBlocked(BottomAirSensorIndex)}"); // True（遮断中）または False（遮断なし）
    Console.WriteLine($"FN1 {frame.IsFn1Pressed}、FN2 {frame.IsFn2Pressed}"); // True（押下中）または False（押下なし）
}
```


### LED の発光指定

`TrySetLedColors()` は、スライダ31箇所と左右の AIR タワー各3箇所の LED 発光を一括で指定できます。以下の例ではスライダ全体を赤、左右の AIR を消灯に設定しています。

```csharp
const int SliderColorCount = 31;
const int AirColorCountPerSide = 3;

var slider = new RgbColor[SliderColorCount];
var leftAir = new RgbColor[AirColorCountPerSide];
var rightAir = new RgbColor[AirColorCountPerSide];

var red = new RgbColor(byte.MaxValue, byte.MinValue, byte.MinValue);
Array.Fill(slider, red);

if (!device.TrySetLedColors(slider, leftAir, rightAir))
{
    Console.WriteLine("LED の発光指定に失敗しました。");
}
```

`true` は色指定を受け付けたことを示します。最後に送信された色設定は通信中に約1秒間隔で再送されます。

### 通信状態の確認

`Status` で通信状態を取得します。

```csharp
Console.WriteLine($"通信状態 {device.Status}"); // Disconnected, Connected, DeviceRemoved, InputReceiveError, LedSendError
```

### 通信の切断

`Disconnect()` は、通信を切断し、同時に入力と LED の発光指定を破棄します。同じインスタンスは `Connect()` で再接続できます。

```csharp
device.Disconnect();
```

### インスタンスの終了

`Dispose()` は通信を終了し、インスタンスを破棄します。再び利用する場合は、新しいインスタンスを作成します。

```csharp
device.Dispose();
```

## ドキュメント

各種 API の詳細な仕様は、以下のリファレンスを参照してください。\
[API リファレンス](docs/api/TasollerPlus.md)

## ライセンス

[MIT LICENSE](https://github.com/Suiraaaa/tasoller-plus-dotnet/blob/main/LICENSE)
