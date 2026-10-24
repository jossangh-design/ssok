namespace Ssok.App;

/// <summary>앱 안 안내문. 실제 동작이 바뀌면 이 글도 함께 고친다.</summary>
internal static class Notices
{
    public const string Privacy =
"""
파일은 이 컴퓨터 안에서만 처리됩니다
쏙은 사진·PDF·한글(hwp·hwpx) 파일의 용량을 이 컴퓨터 안에서 줄이는 프로그램입니다.
파일과 그 내용이 인터넷으로 전송되는 일은 없습니다.

인터넷 연결을 쓰는 경우
쏙은 아래 두 가지 목적으로만 인터넷에 연결합니다.
· 새 버전 확인: 하루 한 번, GitHub(github.com)의 쏙 배포 페이지에서 새 버전이 나왔는지 확인합니다. 왼쪽 설정의 '새 버전이 나오면 알려 주기'를 끄면 확인하지 않습니다.
· 새 버전 내려받기: '새 버전 받기'를 눌렀을 때만 같은 곳에서 설치 파일을 내려받습니다. 내려받은 파일은 쏙 배포용 서명과 지문이 맞을 때만 설치합니다.
연결 과정에서 GitHub에 IP 주소 같은 접속 정보가 전달될 수 있으나, 파일·이름·사용 기록은 전송 대상이 아닙니다.

컴퓨터에 남는 기록
· 오류 확인용 기록(로그)은 %AppData%\Ssok\logs 폴더에 보관되며, 14일이 지나면 자동으로 삭제됩니다.
· 로그에는 파일 크기, 처리 시간, 오류 종류만 남습니다. 파일 이름이나 폴더 위치, 문서 내용은 기록 대상에서 제외됩니다.
· 마지막으로 쓴 설정(목표 용량, 저장 형식 등)은 %AppData%\Ssok\settings.json에 저장됩니다. 파일 이름이나 위치는 담기지 않습니다.
· 줄인 결과는 다운로드할 때까지 이 컴퓨터의 임시 폴더에 잠시 보관되며, 쏙을 닫으면 지워집니다. 프로그램이 비정상적으로 종료되어 남은 파일은 다음 실행 때 정리됩니다.

원본 파일 보호
· 원본은 그대로 두고, 줄인 결과는 다운로드할 때 고른 곳에 '원래이름_쏙' 파일로 저장합니다. 여러 개를 고르면 ZIP 파일 하나로 묶습니다.
· 원본 파일 위에 덮어쓰는 저장은 막혀 있습니다.
· 문서의 글자·표·서식은 손대지 않고, 문서에 들어 있는 그림만 줄입니다.
· '사진 속 위치정보 지우기'를 켜 두면 사진에 담긴 촬영 장소(GPS)와 카메라 정보가 빠진 채로 저장됩니다.
· 암호가 걸린 문서, 배포용 문서, 전자서명이 들어간 문서는 변경 대상에서 제외됩니다.
""";

    public const string Terms =
"""
쏙(Ssok) 이용약관

1. 쏙은 무료 프로그램이며, 학교와 기관에서 자유롭게 나누어 쓸 수 있습니다.
2. 쏙은 '있는 그대로' 제공됩니다. 줄인 파일은 제출 전에 반드시 직접 열어 확인해 주시기 바랍니다.
3. 쏙 사용 중 생긴 파일 손상이나 제출 지연에 대해 개발자는 책임을 지지 않습니다. 단, 원본 파일은 언제나 그대로 남아 있습니다.
4. 쏙의 소스 코드는 MIT 라이선스로 공개되어 있으며, 원문은 아래와 같습니다.
5. 쏙에 함께 포함된 다른 소프트웨어의 라이선스는 '제3자 고지' 탭에서 확인할 수 있습니다.

MIT License

Copyright (c) 2026 Ssok contributors

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
""";

    public const string ThirdParty =
"""
쏙에는 아래 소프트웨어가 함께 포함되어 있습니다. 각 라이선스 원문은 쏙 폴더 안의 licenses 폴더에서 볼 수 있습니다.

.NET 런타임과 WPF (Microsoft / .NET Foundation)
프로그램의 기반 · MIT License · https://github.com/dotnet/runtime · https://github.com/dotnet/wpf

OpenMcdf 3.3.0
hwp 파일 읽기·쓰기 · Mozilla Public License 2.0 · https://github.com/ironfede/openmcdf
쏙은 이 부품을 수정 없이 사용하며, 소스 코드는 위 주소에서 받을 수 있습니다.

qpdf 12.4.2
PDF 파일 읽기·쓰기 · Apache License 2.0 · https://github.com/qpdf/qpdf
Copyright (c) 2005-2021 Jay Berkenbilt, 2022-2026 Jay Berkenbilt and Manfred Holger
qpdf에 포함된 zlib, libjpeg-turbo, sphlib 등의 고지는 licenses/qpdf-NOTICE.md에 있습니다.
qpdf 실행에 필요한 Microsoft Visual C++ 런타임(재배포 허용 파일)도 함께 들어 있습니다.

Microsoft.Windows.SDK.NET (C#/WinRT)
윈도우 내장 기능 연결 · MIT License · https://github.com/microsoft/CsWinRT

사진 처리에는 Windows에 기본으로 들어 있는 이미징 기능(WIC)을, PDF 쪽을 그림으로 바꿀 때는 Windows에 내장된 PDF 렌더러를 사용합니다.

본 제품은 한글과컴퓨터의 한/글 문서 파일(.hwp) 공개 문서를 참고하여 개발하였습니다.
"한글"과 "HWP"는 (주)한글과컴퓨터의 상표이며, 쏙은 한글과컴퓨터와 관계없는 독립 프로그램입니다.
""";
}
