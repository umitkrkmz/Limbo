# 🌌 Limbo

<div align="center">

### Alan Adı Bazlı Profillere Sahip DPI Bypass Motoru

> **Inception filmindeki en derin rüya katmanından adını alır — DPI'nin kaybolduğu yer.**

[![Build & Test](https://github.com/umitkrkmz/Limbo/actions/workflows/build.yml/badge.svg)](https://github.com/umitkrkmz/Limbo/actions/workflows/build.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License](https://img.shields.io/badge/License-GPL%20v3-blue?style=for-the-badge)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?style=for-the-badge&logo=windows)](https://www.microsoft.com/windows)

[🇬🇧 English](README.md) | **🇹🇷 Türkçe**

</div>

---

## 🎯 Limbo Nedir?

Limbo, yeni nesil bir DPI (Deep Packet Inspection / Derin Paket İncelemesi) bypass motorudur ve şunları sunar:

- **Alan Adı Bazlı Profiller** — Her site için farklı bypass stratejisi
- **7 Bypass Stratejisi** — TCP Fragmentation, Smart Fragmentation, Desync, Fake TTL, UDP Fake, ECH, QUIC Fake
- **🔐 ECH (Encrypted Client Hello)** — SNI'yi şifreleyerek DPI'nin hedef alan adını görmesini engeller
- **🛰 Radar Sistemi** — Engellenen alan adlarını otomatik tespit edip bypass profili oluşturur
- **🌍 Genel Mod** — GoodbyeDPI tarzı: sadece profilli alan adlarına değil, her HTTPS/HTTP bağlantısına bypass uygular (profiller yine önceliklidir; yöntemi `None` olan profil bir siteyi hariç tutar)
- **📡 Ağ Analizi** — ISS'nizi tanımlar ve bir alan adı için DNS tamper, RST enjeksiyonu ya da sessiz düşürme raporlayan canlı bir tarama yapar
- **ISP Davranış Analizi** — RST enjeksiyonu, sessiz paket düşürme, DNS zehirlenmesi tespiti
- **Otomatik Kalibrasyon** — Her stratejiyi taramalı bir canlı yakalama üzerinden gerçekten uygulayarak alan adı için ne işe yaradığını bulur, simülasyon değil
- **SIMD Optimizasyonu** — AVX2 hızlandırmalı paket ayrıştırma
- **DNS over HTTPS** — Motor çalışırken tüm uygulamaların DNS sorgularını (IPv4 + IPv6) şifreli DoH ile cevaplar; İSS'nin DNS zehirlemesini sistem ayarlarını değiştirmeden aşar
- **İnteraktif Konsol** — Spectre.Console tabanlı, canlı trafik paneline sahip menü + betiklenebilir CLI

---

## 🛰 Radar Sistemi

Limbo, **Radar** adı verilen otomatik engelleme tespit sistemine sahiptir:

```
Paket Analizi
    ↓
IspBehaviorAnalyzer          ConnectionTracker
 (RST enjeksiyonu)     +      (3sn sessiz düşürme)
    ↓                               ↓
         BlacklistManager (Auto-Blacklist.txt)
                    ↓
         Otomatik Adaptive profili oluşturulur
                    ↓
         Konsol yeni alan adını yansıtır
```

Limbo, ISP'nizin bir alan adını engellediğini tespit ettiğinde otomatik olarak:
1. Radar kara listesine ekler
2. Onun için bir bypass profili oluşturur (yöntem yapılandırılabilir — varsayılan **Adaptive**)
3. Hemen bypass uygulamaya başlar

### 🌍 Genel Mod (GoodbyeDPI tarzı)

Limbo varsayılan olarak yalnızca profili olan (ya da Radar'ın tespit ettiği) alan adlarına dokunur. **Genel mod** ise GoodbyeDPI gibi çalışır: profili olmayan her HTTPS/HTTP bağlantısına **varsayılan yöntem** uygulanır. Bu, Radar'ın kullandığı yöntemin aynısıdır ve **Adaptif Motor (Radar) → Varsayılan Yöntemi Değiştir**'den ayarlanır; `Adaptive`, TCP parçalama olarak çalışır.

- **Profiller yine önceliklidir**, alan adı bazlı ince ayarlar çalışmaya devam eder.
- **Genel modun bozduğu bir siteyi hariç tutmak için** ona yöntemi `None` olan bir profil verin, örn. `limbo add-profile *.bankam.example None`.
- Açmak için: **Adaptif Motor (Radar) → Genel Modu Aç**, config'de `"globalMode": true` ya da tek bir çalıştırma için `limbo run --global`.
- DNS-over-HTTPS her iki modda da aynı şekilde çalışır.

---

## 📡 Ağ Analizi

"ISS'im bu bağlantıya gerçekte ne yapıyor?" sorusunu yanıtlar. İnteraktif menünün **Ağ Analizi** ekranından:

- **Kimin üzerinden bağlandığınız** — genel IP, ISS/kuruluş (ASN) ve kabaca konum, talep üzerine sorgulanır (hiçbir yerel ağ ayarı WAN'ınızı hangi ISS'nin sağladığını göstermez)
- **Yerel ağ bilgisi** — aktif arayüz, sistemde yapılandırılmış DNS sunucuları
- **Bir alan adı için canlı davranış taraması** (yönetici yetkisi gerekir, `run` ile aynı) — sadece o alan adının trafiğinin taramalı, salt-okunur bir yakalamasını açar ve şunları raporlar:
  - **DNS tamper** — sistem DNS'i ile DNS over HTTPS arasında uyuşmazlık
  - **RST enjeksiyonu** — yapay olarak eklenmiş görünen bir inbound reset (pencere boyutu, TTL, RST+ACK heuristikleri)
  - **Sessiz düşürme** — istek gidiyor, hiçbir şey geri gelmiyor
  - **Ulaşılabilir** — müdahale tespit edilmedi

Yönetici yetkisi olmadan sadece ISS/ağ kimliği gösterilir — canlı tarama sessizce başarısız olmak yerine net bir notla atlanır.

---

## 🔧 Bypass Stratejileri

| Strateji | Öncelik | Mekanizma | En İyi Kullanım |
|----------|----------|-----------|----------|
| **ECH** | 200 | HPKE ile SNI'yi tamamen şifreler | Cloudflare siteleri |
| **QuicFake** | 150 | QUIC için sahte datagram enjeksiyonu | HTTP/3, Discord Sesli |
| **TcpFragmentation** | 100 | TCP payload'ını küçük parçalara böler | Genel DPI |
| **SmartFragmentation** | 90 | Tam SNI offset'inde böler (SIMD) | Minimal paket sayısı |
| **Desync** | 80 | Paketleri sırasız gönderir | Durum tabanlı DPI |
| **FakeTtl** | 70 | Düşük TTL'li sahte (decoy) paketler | Pasif DPI |
| **UdpFake** | 60 | UDP/QUIC trafiği için sahte paketler | Sesli/Görüntülü |
| **Adaptive** | — | Tüm stratejileri dener, en iyisini kaydeder | Bilinmeyen siteler |

---

## 🏗 Mimari

```
Limbo/
├── src/
│   ├── Limbo.Console/          # İnteraktif + betiklenebilir CLI uygulaması
│   ├── Limbo.Core/             # Ana iş mantığı
│   │   ├── Analysis/           # Radar, ISP davranış tespiti, ağ analizi, kara liste
│   │   ├── Bypass/             # 7 DPI bypass stratejisi
│   │   ├── Config/             # JSON yapılandırma sistemi
│   │   ├── Detection/          # SIMD paket incelemesi
│   │   ├── Dns/                # DNS over HTTPS + önbellek
│   │   ├── Ech/                # ECH (X25519 + AES-GCM)
│   │   ├── Engine/             # Orkestrasyon + Kalibrasyon
│   │   ├── Logging/            # Thread-safe yapılandırılmış loglama
│   │   ├── Monitoring/         # Gerçek zamanlı bant genişliği izleme
│   │   └── Profiles/           # Alan adı bazlı wildcard profiller
│   └── Limbo.Driver/           # WinDivert P/Invoke sarmalayıcısı
└── tests/
    └── Limbo.Tests/            # xUnit birim testleri
```

---

## 📥 İndirmeler

Hazır derlenmiş ikili dosyalar [Releases](https://github.com/umitkrkmz/Limbo/releases) sayfasında bulunur; [`release.yml`](.github/workflows/release.yml) tarafından otomatik olarak derlenip yayınlanır.

| Paket | Ne zaman? |
|---------|-------------|
| `Limbo-v<sürüm>-setup.exe` | **Önerilen.** Program Files'a kurulur, Başlat menüsüne eklenir ve Program Ekle/Kaldır'dan kaldırılabilir. Kaldırırken ayarlarınızın, profillerinizin ve radar kara listenizin (`%LOCALAPPDATA%\Limbo`) de silinip silinmeyeceğini sorar. |
| `Limbo-Console-v<sürüm>-win-x64.zip` | **Taşınabilir.** İstediğiniz yere çıkartıp `Limbo.Console.exe`'yi çalıştırın; dosyalar aynı klasörde durmalı. |

İkisi de self-contained'dir (.NET kurulumu gerekmez) ve **WinDivert dahildir**. WinDivert yönetici yetkisi gerektirdiği için `Limbo.Console.exe` bu yetkiyi kendisi ister (UAC istemi). Lisans dosyaları (`LICENSE.txt`, `THIRD-PARTY-NOTICES.txt`, `LICENSE-WinDivert.txt`) her ikisinde de bulunur. Aşağıdaki manuel [WinDivert Kurulumu](#%EF%B8%8F-windivert-kurulumu-gerekli) adımları yalnızca **kaynak koddan derlerken** gerekir.

> **Aynı anda tek yakalayıcı:** motoru, kalibrasyonu ya da ağ taramasını aynı anda yalnızca bir Limbo kopyası çalıştırabilir; ikinci kopya bir mesajla reddeder (`--list-profiles` gibi salt okunur komutlar yine çalışır). Limbo, yalnızca Discord'a odaklı kardeş uygulaması [ZeroDivert](https://github.com/umitkrkmz/ZeroDivert) çalışıyorsa da uyarır; ikisi de Discord'un TLS el sıkışmalarını değiştirdiği için birini ya da diğerini kullanın.

> **SHA-256 doğrulaması:** her release'de ZIP'in yanında bir `.sha256` dosyası bulunur. PowerShell'de `Get-FileHash <dosya> -Algorithm SHA256` çıktısını onunla karşılaştırabilirsiniz.

> **Dijital olarak imzalı değildir:** ilk açılışta SmartScreen "Windows bilgisayarınızı korudu" derse **Ek bilgi → Yine de çalıştır** ile devam edebilirsiniz. Windows 11'in **Akıllı Uygulama Denetimi** ise imzasız uygulamaları doğrudan engeller ve uygulama bazında istisna tanımaz; bu özellik *Açık* ise (Windows Güvenliği → Uygulama ve tarayıcı denetimi) Limbo çalışmaz. Özelliği kapatmak sadece Limbo'yu değil tüm uygulamaları etkiler ve tekrar açmak için Windows'u sıfırlamak gerekebilir — kapatmadan önce bunu tartın ya da [kaynak koddan derleyin](#kaynak-koddan-derleme).

---

## 🚀 Hızlı Başlangıç

### Ön Koşullar

- Windows 10/11 64-bit
- **Yönetici (Administrator) yetkisi gerekli** — `Limbo.Console.exe` açılırken bu yetkiyi kendisi ister
- Release ZIP'i için başka bir şey gerekmez (.NET runtime ve WinDivert pakette)
- Kaynak koddan derlemek için: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) ve WinDivert 2.2.2 (aşağıya bakın)

### ⚠️ WinDivert Kurulumu (Gerekli)

Limbo, düşük seviyeli paket yakalama için P/Invoke üzerinden WinDivert kullanır. WinDivert ayrı lisanslıdır (LGPL v3 / GPL v2) ve bu repoya **commit edilmemiştir** — aşağıdaki adımlar yalnızca kaynak koddan derlerken gereklidir. Hazır derlenmiş release ZIP'lerinde WinDivert zaten dahildir (bkz. yukarıdaki "İndirmeler" bölümü).

> **Kaynak koddan derliyorsanız:** Derlemeden önce `WinDivert.dll` ve `WinDivert64.sys` dosyalarını `src/Limbo.Driver/runtimes/win-x64/native/` klasörüne yerleştirin. MSBuild bunları otomatik olarak çıktı klasörüne kopyalayacaktır.

**Adım 1 — WinDivert 2.2.2'yi indirin:**

> https://github.com/basil00/WinDivert/releases/tag/v2.2.2

`WinDivert-2.2.2-A.zip` dosyasını indirip çıkarın.

**Adım 2 — Dosyaları çalıştırılabilir dosyanın yanına kopyalayın:**

Çıkardığınız ZIP'ten `x64` varyantlarını kopyalayın:

```
WinDivert-2.2.2-A/
└── x64/
    ├── WinDivert.dll      ← bunu kopyalayın
    └── WinDivert64.sys    ← bunu kopyalayın
```

Her iki dosyayı da `Limbo.Console.exe` ile aynı klasöre yerleştirin:

```
Limbo.Console.exe
WinDivert.dll        ← buraya
WinDivert64.sys      ← buraya
```

> **Neden özellikle 2.2.2?** Limbo'nun P/Invoke tanımları (`WinDivertNative.cs`) WinDivert 2.2 API'sini hedefler. Eski sürümlerin (1.x, 2.0) farklı fonksiyon imzaları vardır ve **çalışmaz**. Daha yeni minor sürümler (2.2.x) uyumlu olmalıdır.

### Kaynak Koddan Derleme

```bash
git clone https://github.com/umitkrkmz/Limbo.git
cd Limbo
dotnet build -c Release
```

Release'tekiyle aynı self-contained paketi (runtime gömülü tek bir `Limbo.Console.exe`, yanında WinDivert ve lisans dosyaları) üretmek için publish profilini kullanın:

```bash
dotnet publish src/Limbo.Console -p:PublishProfile=win-x64
```

Çıktı `src/Limbo.Console/bin/publish/win-x64/` klasörüne düşer.

**Kurulum dosyası (isteğe bağlı):** [Inno Setup 6](https://jrsoftware.org/isinfo.php) kuruluysa, yukarıdaki publish komutundan sonra çalıştırın. Kurulum dosyası `installer\Output\` klasörüne düşer:

```powershell
# Inno Setup "herkes için" kurulduysa:
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DAppVersion=1.1.0 installer\Limbo.iss
# Sadece kendi kullanıcınız için kurulduysa (ör. winget --scope user):
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" /DAppVersion=1.1.0 installer\Limbo.iss
```

**Yeni sürüm yayınlamak:** üç csproj'daki `<Version>` değerini güncelleyin, CHANGELOG'a sürüm başlığını ekleyin, sonra `v` ile başlayan bir tag pushlayın (`git tag v1.2.0 && git push origin v1.2.0`). [Release iş akışı](.github/workflows/release.yml) tag'in sürümle uyuştuğunu kontrol eder, kurulum dosyasını ve taşınabilir ZIP'i SHA-256'larıyla birlikte derler ve **taslak (draft)** bir GitHub Release oluşturur; notları düzenleyip **Publish**'e basmak size kalır. `v1.2.0-rc.1` gibi ekli tag'ler pre-release olarak açılır.

### Çalıştırma

```powershell
# Yönetici terminalinden - interaktif menüyü açar
dotnet run --project src/Limbo.Console

# Ya da betiklenebilir bir CLI olarak kullanın
dotnet run --project src/Limbo.Console -- --help
```

> `Limbo.Console.exe` yönetici yetkisi isteyen bir manifest taşıdığı için, yönetici **olmayan** bir terminalden `dotnet run` "İstenen işlem için yükseltme gerekiyor" hatasıyla başarısız olur. Terminali Yönetici olarak açın ya da WinDivert'e dokunmayan komutlar için derlenmiş DLL'i `dotnet` üzerinden çalıştırın (`dotnet src/Limbo.Console/bin/Debug/net10.0/Limbo.Console.dll --help`).

---

## 🖥 CLI Kullanımı

```
Usage: limbo [command] [options]

Commands:
  run [--global]               DPI bypass motorunu başlatır (--global: bu çalıştırmada tüm trafik)
  calibrate <domain> [--save]  Bir alan adı için en iyi bypass yöntemini bulur (--save: sormadan kaydeder)
  test <domain>                DNS, DoH, TCP bağlantısını test eder
  add-profile <domain> <method> [opts]   Alan adı profili ekler
  remove-profile <domain>      Alan adı profilini kaldırır
  radar                        Otomatik tespit edilen engellenmiş alan adlarını gösterir
  radar-import <file>          Radar kara listesi içe aktarır
  radar-export <file>          Radar kara listesini dışa aktarır
  radar-clear [--yes]          Tüm radar kayıtlarını temizler (--yes: onay sormaz)

add-profile için seçenekler:
  --frag <n>       Fragment boyutu (TcpFragmentation / SmartFragmentation)
  --ttl <n>        Sahte TTL değeri (FakeTtl / UdpFake)
  --split <n>      Split pozisyonu (Desync)
  --offset <n>     SNI split offset'i (SmartFragmentation)
  --ech-name <s>   ECH public name (Ech)
  --quic-size <n>  Sahte paket boyutu (QuicFake)

Genel seçenekler:
  -v, --verbose    Ayrıntılı loglama
  -c, --config     Özel config dosya yolu
  --list-profiles  Tüm profilleri listeler
  --list-methods   Öncelikleriyle bypass yöntemlerini listeler
  --show-config    Mevcut yapılandırmayı gösterir
  --version        Sürüm bilgisi
```

**Örnekler:**

```powershell
# Ayrıntılı çıktıyla bypass motorunu başlat
limbo run -v

# Bir alan adı için en iyi yöntemi bul
limbo calibrate discord.com

# Profili manuel ekle
limbo add-profile "*.discord.com" TcpFragmentation --frag 2

# Bir alan adının engelli olup olmadığını test et
limbo test twitter.com

# Radar tarafından tespit edilen engellenmiş alan adlarını göster
limbo radar

# Topluluk kara listesini içe aktar
limbo radar-import blocked.txt
```

---

## 🖥 İnteraktif Menü

`limbo` argümansız çalıştırıldığında, Spectre.Console üzerine kurulu tam bir interaktif dashboard açılır. Durum paneli (mod, yönetici, DoH, ağ/DNS, profil/Radar) logonun yanında durur ve menü **Motor / Yapılandırma / Araçlar / Limbo** olarak gruplanır; böylece ekranın tamamı varsayılan 30 satırlık konsol penceresine sığar:

- **Motoru Başlat** — hangi modda başlayacağını gösterir; bypass motoru çalışırken canlı güncellenen trafik tablosu (paketler, eşleşen/değiştirilen, bant genişliği, DoH ile cevaplanan DNS)
- **Modu Değiştir** — *sadece profiller* ile *genel* (GoodbyeDPI tarzı) mod arasında tek adımda geçiş
- **Nasıl Kullanılır** — yerleşik rehber: hızlı başlangıç, modlar, profiller ve hariç tutma, DNS over HTTPS ve sorun giderme listesi
- **Profil Yönetimi** — seçilen yönteme özel parametreleri soran bir sihirbazla profil listeleme/ekleme/düzenleme/silme
- **DNS Ayarları** — DoH aç/kapa, sağlayıcı seç (Cloudflare/Google/Quad9/özel), sistemin gerçekte kullandığı DNS sunucularını görme (IPv4 ve IPv6 ayrı ayrı; IPv6 DNS sunucuları etkinken DoH kapalıysa uyarı - yalnızca IPv4 için yapılmış DNS ayarı onları kapsamaz), canlı çözümleme testi
- **Adaptif Motor (Radar)** — otomatik tespiti aç/kapa, **genel modu** aç/kapa, tespit edilen alan adlarında ve genel modda kullanılan varsayılan bypass yöntemini (ve parametrelerini) seçme, tespitleri listele/içe-dışa aktar/temizle
- **Ağ Analizi** — ISS/genel IP kimliği + bir alan adı için DNS tamper, RST enjeksiyonu ya da sessiz düşürme arayan talep-üzerine canlı tarama
- **Kalibrasyon** ve **Bağlantı Testi** — CLI komutlarıyla aynı kontroller, ilerleme çubuğu/sonuç tablosuyla
- **Genel Ayarlar** — ayrıntılı loglama, log dosyası yolu, **dil** (İngilizce / Türkçe) ve güncelleme kontrolü
- **Hakkında** — sürüm, lisans, kaynak kod bağlantısı, paketteki bileşenler ve güncelleme durumu (daha yeni bir sürüm varsa indirme sayfasına kısayol)

**Güncelleme kontrolü:** interaktif menü açılırken Limbo, son sürümü öğrenmek için GitHub API'sine (`api.github.com`) anonim tek bir istek yapar. Daha yeni bir sürüm varsa durum paneli ve Hakkında ekranı bunu gösterir; indirme ve kurulum her zaman size kalır, Limbo kendini kendiliğinden güncellemez. Betiklenebilir CLI komutları bu isteği hiç yapmaz ve **Genel Ayarlar**'dan kapatılabilir. Bunun dışında Limbo yalnızca kullandığınız özellikler kapsamında servislere bağlanır: ayarladığınız DoH sunucusu, Ağ Analizi ekranını açtığınızda `ipinfo.io` ve test ya da kalibre ettiğiniz alan adları.

Bunların hepsi betiklenebilir, interaktif olmayan komutlar olarak da mevcuttur (`limbo run`, `limbo add-profile ...` vb.) — aşağıdaki [CLI Kullanımı](#-cli-kullanımı) bölümüne bakın.

---

## ⚙️ Yapılandırma

Config dosyası: `%LOCALAPPDATA%\Limbo\config.json`

```json
{
  "filter": "tcp or udp",
  "verbose": false,
  "dns": {
    "useDoH": true,
    "doHServer": "https://1.1.1.1/dns-query"
  },
  "language": "English",
  "profiles": [
    {
      "domain": "*.discord.com",
      "method": "TcpFragmentation",
      "fragmentSize": 2
    },
    {
      "domain": "*.twitter.com",
      "method": "Ech"
    }
  ]
}
```

**Veri dosyaları:**
- Profiller: `%LOCALAPPDATA%\Limbo\profiles\*.json`
- Radar kara listesi: `%LOCALAPPDATA%\Limbo\Auto-Blacklist.txt`

---

## 🌐 DNS over HTTPS

İSS **DNS'i de zehirliyorsa** SNI'yi gizlemek yetmez: engelli bir alan adı engelleme sayfasının adresine çözümlenir (Türk Telekom `discord.com` için `195.175.254.2` döndürür) ve el sıkışma nasıl gizlenirse gizlensin bağlantı yanlış sunucuya gider.

DoH açıkken (yeni config'lerde varsayılan), motor çalıştığı sürece Limbo **tüm uygulamaların düz DNS sorgularını** (UDP 53, **IPv4 ve IPv6**) yakalar ve ağa çıkmalarına izin vermeden şifreli DNS-over-HTTPS üzerinden cevaplar:

- **Sistem ayarlarına dokunulmaz.** Limbo durduğunda ya da kaldırıldığında geri alınacak bir şey kalmaz.
- **IPv6 da kapsanır.** Sık görülen bir tuzak: Windows'ta "şifreli DNS" yalnızca IPv4 için ayarlıdır, modem ise IPv6 üzerinden kendi DNS sunucusunu dağıtır ve sorgular sessizce zehirli olan o yoldan gider. Limbo ikisini de cevaplar.
- **Genel DNS sunucularına düz DNS'in engellendiği hatlarda da çalışır.** Bazı İSS'ler `1.1.1.1`'e giden UDP 53'ü düşürür; DoH ise HTTPS (443) üzerinden gider.
- **Güvenli şekilde geri çekilir:** DoH sunucusuna ulaşılamazsa asıl sorgu olduğu gibi gönderilir; DNS çalışmaya devam eder, sadece korumasız. Canlı panel kaç sorgunun DoH ile cevaplandığını, kaçının düz DNS'e döndüğünü gösterir.

Desteklenen sunucular (her RFC 8484 sunucusu çalışır; sunucu destekliyorsa HTTP/2 kullanılır):
- Cloudflare: `https://1.1.1.1/dns-query` (varsayılan; IP adresi olduğu için kendi adını çözümlemeye ihtiyaç duymaz)
- Google: `https://dns.google/dns-query`
- Quad9: `https://dns.quad9.net/dns-query`

> Bunun için motorun WinDivert filtresinin UDP'yi içermesi gerekir (varsayılan `tcp or udp` içerir). Kendi DNS'ini zaten şifreleyen DoH açık tarayıcılar her iki durumda da etkilenmez.

---

## 📊 GoodbyeDPI ile Karşılaştırma

| Özellik | GoodbyeDPI | Limbo |
|---------|------------|-------|
| Hedef | Tüm trafik, global seçenekler | Alan adı bazlı profiller ya da genel modda tüm trafik |
| ECH Desteği | ⚠️ Sadece ECH grease (sahte paketler) | ✅ Tam X25519 HPKE (gerçek ECH) |
| Profil Sistemi | Yok (sadece global bayraklar) | ✅ Alan adı başına wildcard desenler |
| Radar (otomatik tespit) | Yok | ✅ Var |
| ISP Davranış Analizi | Yok | ✅ RST enjeksiyonu, sessiz düşürmeler |
| Otomatik Kalibrasyon | Yok | ✅ Alan adı başına |
| Bant Genişliği İzleme | Yok | ✅ Gerçek zamanlı |
| DNS Bypass | ✅ UDP DNS'i başka bir sunucu/porta yönlendirir (`--dns-addr`, şifresiz) | ✅ Tüm uygulamaların DNS'ini (IPv4 + IPv6) şifreli DoH ile cevaplar |
| SIMD Hızlandırma | Yok | ✅ AVX2 |
| CLI | ✅ Var | ✅ İnteraktif + betiklenebilir |
| Dil | C (C99) | C# (.NET 10) |
| Platform | Windows | Windows |
| Lisans | Apache 2.0 | GPL v3 (veya sonrası) |

Limbo, **GoodbyeDPI'den ilham almıştır** ancak soruna farklı bir yaklaşımla yaklaşır: tek bir global yapılandırma yerine, otomatik tespit ve adaptif kalibrasyona sahip alan adı bazlı stratejiler kullanır.

---

## 🗺 Yol Haritası

### ✅ Tamamlananlar (v1.0)
- [x] 7 bypass stratejisi + Adaptive mod
- [x] ECH (Encrypted Client Hello) — tam X25519 HPKE
- [x] QUIC / HTTP3 bypass
- [x] Radar sistemi — otomatik tespit + otomatik kara liste
- [x] ISP davranış analizi (RST enjeksiyonu, sessiz düşürmeler)
- [x] Wildcard destekli alan adı bazlı profiller
- [x] Alan adı başına otomatik kalibrasyon
- [x] Profil içe/dışa aktarma (JSON)
- [x] Gerçek zamanlı bant genişliği izleme
- [x] DNS over HTTPS (paralel A + AAAA)
- [x] SIMD (AVX2) paket ayrıştırma
- [x] Canlı trafik paneline sahip interaktif konsol (Spectre.Console)
- [x] İngilizce / Türkçe dil seçimi
- [x] Ağ Analizi — ISS kimliği + canlı DNS tamper/RST enjeksiyonu/sessiz düşürme taraması
- [x] Kalibrasyon her stratejiyi gerçekten canlı uyguluyor (taramalı yakalama), simülasyon değil
- [x] Radar'ın otomatik tespit edilen domainleri için yapılandırılabilir varsayılan yöntem
- [x] Tam CLI (run, calibrate, test, radar, add/remove-profile)
- [x] Birim test paketi + CI (her push/PR'da build & test)


---

## 🤝 Katkıda Bulunma

Katkılar memnuniyetle karşılanır! İşte nasıl:

1. Bu repoyu **fork**'layın
2. **Bir branch oluşturun** — `git checkout -b feature/my-feature`
3. **Değişikliklerinizi commit'leyin** — `git commit -m "Add: my feature"`
4. **Push'layın** — `git push origin feature/my-feature`
5. **Pull Request açın**

**Başlamak için iyi konular:**
- ISP parmak izi veritabanı girdileri
- Yeni bypass stratejisi fikirleri
- Test kapsamı iyileştirmeleri
- Dokümantasyon / çeviriler

Büyük değişiklikler üzerinde çalışmadan önce, yaklaşımı birlikte tartışabilmemiz için lütfen bir **Issue** açın.

---

## ⚠️ Sorumluluk Reddi

> Bu yazılım **yalnızca eğitim ve araştırma amaçlıdır**.
> Kullanım tamamen kendi sorumluluğunuzdadır. Kullanmadan önce yerel yasalarınızı kontrol edin.
> Yazarlar herhangi bir kötüye kullanımdan sorumlu değildir.

---

## 🙏 Teşekkürler

- **[basil00/WinDivert](https://github.com/basil00/WinDivert)** — Windows ağ paketi yakalama ve değiştirme sürücüsü. Limbo, düşük seviyeli paket yakalama için WinDivert kullanır. LGPL v3 / GPL v2 altında lisanslanmıştır.
- **[ValdikSS/GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI)** — DPI bypass tekniklerinin orijinal ilham kaynağı. Limbo farklı bir mimari yaklaşım benimser (alan adı bazlı adaptif profiller, Radar sistemi, ECH) ancak temel fikirler GoodbyeDPI'nin mükemmel çalışmasından gelir. Apache 2.0 altında lisanslanmıştır.

---

## 💬 Topluluk

Bir sorunuz, fikriniz var mı veya Limbo'yu nasıl kullandığınızı paylaşmak mı istiyorsunuz?

- **[GitHub Discussions](https://github.com/umitkrkmz/Limbo/discussions)** — Sorular, fikirler, göster & anlat
- **[GitHub Issues](https://github.com/umitkrkmz/Limbo/issues)** — Hata raporları ve özellik istekleri

---

## 📄 Lisans

Copyright (C) 2025-2026 Umit Korkmaz

Bu proje **GNU General Public License v3.0 veya sonraki bir sürümü** (`GPL-3.0-or-later`) ile lisanslanmıştır: Free Software Foundation tarafından yayımlanan GPL'in 3. sürümü ya da (tercihinize bağlı olarak) daha sonraki herhangi bir sürümü kapsamında yeniden dağıtabilir ve/veya değiştirebilirsiniz. Bu program faydalı olması umuduyla dağıtılmaktadır, ancak **hiçbir garanti verilmez**; ayrıntılar için lisans metnine bakın.

> Limbo, LGPL v3 / GPL v2 çift lisanslı [WinDivert](https://github.com/basil00/WinDivert)'i **LGPL v3** koşulları altında kullanır (LGPL v3, GPL v3 ile uyumludur).

> **Önceki sürümler:** v1.0.0 ve v1.1.0 **GPL v2** ile yayımlanmıştır ve o sürümlerin kopyaları GPL v2 altında kalmaya devam eder. GPL v3'e geçiş bir sonraki sürümden itibaren geçerlidir.

Tam lisans metni için [LICENSE](LICENSE) dosyasına bakın. Release paketlerinde birlikte dağıtılan üçüncü taraf bileşenler (WinDivert, Spectre.Console, BouncyCastle, .NET runtime) ve lisansları [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) dosyasında listelenmiştir; bu dosya her release ZIP'inin içinde de bulunur.

---

<div align="center">

**⭐ Faydalı bulduysanız bu repoya yıldız verin!**

*Tasarım & Geliştirme: [Umit Korkmaz](https://github.com/umitkrkmz)*

</div>
