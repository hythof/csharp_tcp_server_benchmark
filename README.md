# CSarpで高速なTCP Serverの実装を行うための調査
CSharpでTCP Serverを実装するためのアプローチが複数提供される中で、現時点ではawait/asyncのモデルがパフォーマンスとメンテナンスの両面おいて優れていると聞く  
それが「どの程度」優れているかを調査する

## 比較対象
1. await/asyncを使った非同期モデル(非同期TcpClient)
2. BeginRecieve()などを使った非同期モデル(非同期Socket)
3. ThreadPoolを使った同期的モデル(同期ThreadPool)

## 検証環境
同時接続数            : 1,000接続
パケット本体          : 1,000byte
1接続辺りリクエスト数 : 1,000回
CPU   : Core i7 3770
Memory: 16G
OS    : Windows 7

## 検証結果
|実装方法      |行数|難易度|秒間リクエスト数|
|-------------|-------|-------|-------|-------|
|async/await  |101|易しい|48,955|
|非同期ソケット|170|難しい|61,624|
|ThreadPool   |97|易しい|51,235|

## 考察
1. ThreadPoolはThreadPool.SetMinThreads()でworkerスレッドを同時接続数と同等程度に設定しないと上記の性能が出ない
2. async/awaitは予想よりも遅い、特に非同期ソケットと比べると有意な差が見られる

