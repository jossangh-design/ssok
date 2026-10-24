// 쏙 업데이트 서명 키 만들기 (.NET 10: dotnet run make-update-key.cs)
// 비밀 키는 GitHub 저장소의 비밀값(SSOK_UPDATE_KEY)에만 넣고, 다른 곳에 올리지 마세요.
using System.Security.Cryptography;

var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
var file = Path.Combine(docs, "쏙-업데이트-비밀키.txt");
if (File.Exists(file))
{
    Console.WriteLine($"이미 키가 있습니다: {file}");
    Console.WriteLine("새로 만들면 이미 설치된 쏙이 업데이트를 받지 못하게 됩니다. 새로 만들려면 그 파일을 지운 뒤 다시 실행하세요.");
    return 1;
}

using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var privateKey = Convert.ToBase64String(key.ExportPkcs8PrivateKey());
var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
File.WriteAllText(file,
    "쏙 업데이트 서명용 비밀 키 — 다른 사람에게 보내거나 인터넷에 올리지 마세요.\n" +
    "GitHub 저장소 → Settings → Secrets and variables → Actions → New repository secret\n" +
    "  Name: SSOK_UPDATE_KEY\n" +
    "  Secret: 아래 한 줄 전체\n\n" +
    privateKey + "\n\n" +
    "(참고) 공개 키: " + publicKey + "\n");

Console.WriteLine("키를 만들었습니다.");
Console.WriteLine($"  {file}");
Console.WriteLine("이 파일을 열어 안내대로 GitHub 비밀값(SSOK_UPDATE_KEY)에 넣어 주세요.");
Console.WriteLine("파일은 USB 등에 따로 보관해 두세요. 잃어버리면 기존 사용자에게 자동 업데이트를 보낼 수 없습니다.");
return 0;
