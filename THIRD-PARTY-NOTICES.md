# 제3자 고지

쏙 배포 파일에는 아래 소프트웨어가 들어 있습니다. 라이선스 원문은 `licenses/` 폴더에 있습니다.

| 부품 | 용도 | 라이선스 | 원문 |
| --- | --- | --- | --- |
| .NET 런타임, WPF | 프로그램 바탕 | MIT | 아래 |
| [OpenMcdf](https://github.com/ironfede/openmcdf) 3.3.0 | hwp(복합 파일) 읽기·쓰기 | MPL-2.0 | `licenses/OpenMcdf-LICENSE.txt` |
| [qpdf](https://github.com/qpdf/qpdf) 12.4.2 | PDF 읽기·쓰기 | Apache-2.0 | `licenses/qpdf-LICENSE.txt`, `licenses/qpdf-NOTICE.md` |
| Microsoft Visual C++ 런타임 | qpdf 실행 | Microsoft 재배포 허용 파일 | qpdf 배포본에 포함 |
| Microsoft.Windows.SDK.NET (C#/WinRT 연결 부품) | 윈도우 내장 PDF 렌더러 사용 | MIT | 아래 .NET 런타임과 같은 MIT 원문 |

- OpenMcdf는 고치지 않고 NuGet 패키지 그대로 씁니다. 소스는 위 저장소에서 받을 수 있습니다 (MPL-2.0 3.2항).
- qpdf는 공식 배포본(`qpdf-12.4.2-msvc64.zip`, SHA-256 `db87077e683630c1217e0e8f9a20a9749d952ab676e881c3689187763a5de25d`)의 실행 파일을 그대로 씁니다.
- 사진 처리는 Windows에 내장된 이미징 기능(WIC)을 쓰며 별도로 포함하지 않습니다.
- PDF 쪽을 그림으로 바꾸는 기능은 Windows 10·11에 내장된 PDF 렌더러(Windows.Data.Pdf)를 쓰며 별도로 포함하지 않습니다.
- 본 제품은 한글과컴퓨터의 한/글 문서 파일(.hwp) 공개 문서를 참고하여 개발하였습니다. "한글", "HWP"는 (주)한글과컴퓨터의 상표이며, 쏙은 한글과컴퓨터와 관계없는 독립 프로그램입니다.
- 시험용 파일의 출처는 `tests/fixtures/README.md`에 있습니다 (배포 파일에는 들어가지 않음).

## .NET 런타임, WPF

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

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

## 쓰지 않기로 한 부품

| 부품 | 이유 |
| --- | --- |
| PDFsharp 7 | 2026년 7.0 미리보기부터 AGPL·상용 이중 라이선스로 바뀜 |
| Ghostscript, iText, pyhwp | AGPL |
