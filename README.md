# CSarpで高速なTCP Serverの実装を行うための調査
CSharpでTCP Serverを実装するためのアプローチが複数提供される中で、現時点ではawait/asyncのモデルがパフォーマンスとメンテナンスの両面おいて優れていると聞く  
それが「どの程度」優れているかを調査する

## 比較対象
1. await/asyncを使った非同期モデル(非同期TcpClient)
2. BeginRecieve()などを使った非同期モデル(非同期Socket)
3. ThreadPoolを使った同期的モデル(同期ThreadPool)

## AWS検証環境
| PC       | EC2インスタンスタイプ |
| クライアント | c4.xlarge        |
| サーバ    | c4.xlarge         |

## 検証結果
| 項目名          | リクエスト/秒 | 行数 | メンテナンス性 |
| 非同期TcpClient |            |      |             |
| 非同期Socket    |            |      |             |
| 同期ThreadPool  |            |      |             |

## 考察
