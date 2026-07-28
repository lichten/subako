# Third-Party Notices

Subako は以下のサードパーティソフトウェアを利用しています。各コンポーネントの
ライセンス条文は本ファイル末尾に収録しています。

This software includes the following third-party components.

## ビューア (.NET) が配布物に含むもの

| コンポーネント | バージョン | ライセンス | 著作権 |
|---|---|---|---|
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MIT | © .NET Foundation and Contributors |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) | 10.0.10 | MIT | © .NET Foundation and Contributors |
| [Microsoft.Xaml.Behaviors.Wpf](https://github.com/microsoft/XamlBehaviorsWpf) | 1.1.142 | MIT | © Microsoft Corporation |
| [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) (bundle_e_sqlite3) | 3.0.2 | Apache-2.0 | © SourceGear, LLC |
| [SQLite](https://www.sqlite.org/) (SQLitePCLRaw 経由のネイティブライブラリ) | — | Public Domain | — |
| [Emoji.Wpf](https://github.com/samhocevar/emoji.wpf) | 0.3.4 | WTFPL | © Sam Hocevar |
| [Stfu](https://github.com/samhocevar/stfu) (Emoji.Wpf 経由) | 0.1.1 | WTFPL | © Sam Hocevar |
| [Typography](https://github.com/LayoutFarm/Typography) (OpenFont / GlyphLayout、Emoji.Wpf に同梱) | — | MIT (一部 Apache-2.0 / FreeType License 等の混在)¹ | © LayoutFarm and contributors |
| [Twemoji](https://github.com/twitter/twemoji) 絵文字アートワーク (Emoji.Wpf 同梱の Twemoji Mozilla フォント内) | — | CC-BY 4.0 | © Twitter, Inc. and other contributors |

¹ Typography プロジェクトは複数の許諾型ライセンスのコードを含む
(Apache-2.0: Samuel Carlsson, Apache/PDFBox Authors, Adobe AFDKO / MIT: Michael
Popoloski (SharpFont) / FreeType Project License ほか)。詳細は
[LICENSE.md](https://github.com/LayoutFarm/Typography/blob/master/LICENSE.md) を参照。

## 取得ツール (Python) が利用するもの (配布物には含まれない。利用者が pip で導入)

| コンポーネント | ライセンス | 著作権 |
|---|---|---|
| [requests](https://github.com/psf/requests) | Apache-2.0 | © Kenneth Reitz and contributors |
| [python-dotenv](https://github.com/theskumar/python-dotenv) | BSD-3-Clause | © Saurabh Kumar |

開発時のみ使用するもの (xunit, pytest, coverlet, Microsoft.NET.Test.Sdk) は
配布物に含まれないため本ファイルの対象外。

---

## ライセンス条文

### MIT License

対象: CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, Microsoft.Xaml.Behaviors.Wpf,
Typography (プロジェクト全体のライセンスとして)

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Apache License 2.0

対象: SQLitePCLRaw, requests

条文全文: <https://www.apache.org/licenses/LICENSE-2.0>

```
Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```

(バイナリ配布物には Apache License 2.0 の条文全文を同梱すること —
docs/release-plan.md §4-1 の publish 設定で本ファイルとともに含める)

### WTFPL — Do What The Fuck You Want To Public License

対象: Emoji.Wpf, Stfu

```
            DO WHAT THE FUCK YOU WANT TO PUBLIC LICENSE
                    Version 2, December 2004

 Copyright (C) 2004 Sam Hocevar <sam@hocevar.net>

 Everyone is permitted to copy and distribute verbatim or modified
 copies of this license document, and changing it is allowed as long
 as the name is changed.

            DO WHAT THE FUCK YOU WANT TO PUBLIC LICENSE
   TERMS AND CONDITIONS FOR COPYING, DISTRIBUTION AND MODIFICATION

  0. You just DO WHAT THE FUCK YOU WANT TO.
```

### BSD 3-Clause License

対象: python-dotenv

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### Creative Commons Attribution 4.0 International (CC-BY 4.0)

対象: Twemoji 絵文字アートワーク (Copyright 2019 Twitter, Inc and other
contributors)。本アプリの絵文字表示は Emoji.Wpf が同梱する Twemoji Mozilla
フォントを通じて Twemoji のグラフィックを使用している。

条文全文: <https://creativecommons.org/licenses/by/4.0/>
