# 🌌 Limbo

<div align="center">

### Alan Adı Bazlı Profillere Sahip DPI Bypass Motoru

> **Inception filmindeki en derin rüya katmanından adını alır — DPI'nin kaybolduğu yer.**

[![Build & Test](https://github.com/umitkrkmz/Limbo/actions/workflows/build.yml/badge.svg)](https://github.com/umitkrkmz/Limbo/actions/workflows/build.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License](https://img.shields.io/badge/License-GPL%20v2-blue?style=for-the-badge)](LICENSE)
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
- **📡 Ağ Analizi** — ISS'nizi tanımlar ve bir alan adı için DNS tamper, RST enjeksiyonu ya da sessiz düşürme raporlayan canlı bir tarama yapar
- **ISP Davranış Analizi** — RST enjeksiyonu, sessiz paket düşürme, DNS zehirlenmesi tespiti
- **Otomatik Kalibrasyon** — Her stratejiyi taramalı bir canlı yakalama üzerinden gerçekten uygulayarak alan adı için ne işe yaradığını bulur, simülasyon değil
- **SIMD Optimizasyonu** — AVX2 hızlandırmalı paket ayrıştırma
- **DNS over HTTPS** — ISP DNS engellemesini atlatma
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

| Paket | Açıklama |
|---------|-------------|
| `Limbo-Console-<sürüm>-win-x64.zip` | CLI uygulaması (self-contained, .NET kurulumu gerekmez) — **WinDivert dahildir**, doğrudan çalıştırılabilir |

> **İndirdikten sonra:** ZIP'i çıkarıp `Limbo.Console.exe`'yi Yönetici olarak çalıştırmanız yeterli — `WinDivert.dll`, `WinDivert64.sys` ve WinDivert'in lisansı (`WinDivert-LICENSE.txt`) zaten pakette. Aşağıdaki manuel [WinDivert Kurulumu](#%EF%B8%8F-windivert-kurulumu-gerekli) adımları yalnızca **kaynak koddan derlerken** gerekir.

> **SHA-256 checksum** değerleri doğrulama için her release açıklamasında listelenir.

---

## 🚀 Hızlı Başlangıç

### Ön Koşullar

- Windows 10/11 64-bit
- .NET 10.0 Runtime
- **Yönetici (Administrator) yetkisi gerekli**
- WinDivert 2.2.2 (aşağıya bakın)

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

### Çalıştırma

```powershell
# Yönetici olarak çalıştırın - interaktif menüyü açar
dotnet run --project src/Limbo.Console

# Ya da betiklenebilir bir CLI olarak kullanın
dotnet run --project src/Limbo.Console -- --help
```

---

## 🖥 CLI Kullanımı

```
Usage: limbo [command] [options]

Commands:
  run                          DPI bypass motorunu başlatır
  calibrate <domain>           Bir alan adı için en iyi bypass yöntemini bulur
  test <domain>                DNS, DoH, TCP bağlantısını test eder
  add-profile <domain> <method> [opts]   Alan adı profili ekler
  remove-profile <domain>      Alan adı profilini kaldırır
  radar                        Otomatik tespit edilen engellenmiş alan adlarını gösterir
  radar-import <file>          Radar kara listesi içe aktarır
  radar-export <file>          Radar kara listesini dışa aktarır
  radar-clear                  Tüm radar kayıtlarını temizler

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

`limbo` argümansız çalıştırıldığında, Spectre.Console üzerine kurulu tam bir interaktif dashboard açılır:

- **Motoru Başlat** — bypass motoru çalışırken canlı güncellenen trafik tablosu (paketler, eşleşen/değiştirilen, bant genişliği)
- **Profil Yönetimi** — seçilen yönteme özel parametreleri soran bir sihirbazla profil listeleme/ekleme/düzenleme/silme
- **DNS Ayarları** — DoH aç/kapa, sağlayıcı seç (Cloudflare/Google/Quad9/özel), sistemin gerçek yapılandırılmış DNS sunucularını görme, canlı çözümleme testi
- **Adaptif Motor (Radar)** — otomatik tespiti aç/kapa, yeni tespit edilen alan adlarının hangi bypass yöntemini (ve parametrelerini) alacağını seçme, tespitleri listele/içe-dışa aktar/temizle
- **Ağ Analizi** — ISS/genel IP kimliği + bir alan adı için DNS tamper, RST enjeksiyonu ya da sessiz düşürme arayan talep-üzerine canlı tarama
- **Kalibrasyon** ve **Bağlantı Testi** — CLI komutlarıyla aynı kontroller, ilerleme çubuğu/sonuç tablosuyla
- **Genel Ayarlar** — ayrıntılı loglama, log dosyası yolu, ve **dil** (İngilizce / Türkçe)

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

Desteklenen sunucular:
- Cloudflare: `https://1.1.1.1/dns-query`
- Google: `https://dns.google/dns-query`
- Quad9: `https://dns.quad9.net/dns-query`

Limbo, paralel A + AAAA sorguları çalıştırır ve IPv6 adreslerine öncelik verir.

---

## 📊 GoodbyeDPI ile Karşılaştırma

| Özellik | GoodbyeDPI | Limbo |
|---------|------------|-------|
| Hedef | Tüm trafik, global seçenekler | Alan adı bazlı profiller |
| ECH Desteği | ⚠️ Sadece ECH grease (sahte paketler) | ✅ Tam X25519 HPKE (gerçek ECH) |
| Profil Sistemi | Yok (sadece global bayraklar) | ✅ Alan adı başına wildcard desenler |
| Radar (otomatik tespit) | Yok | ✅ Var |
| ISP Davranış Analizi | Yok | ✅ RST enjeksiyonu, sessiz düşürmeler |
| Otomatik Kalibrasyon | Yok | ✅ Alan adı başına |
| Bant Genişliği İzleme | Yok | ✅ Gerçek zamanlı |
| DNS Bypass | ⚠️ Sadece UDP yönlendirme (DoH yok) | ✅ DNS over HTTPS (DoH) |
| SIMD Hızlandırma | Yok | ✅ AVX2 |
| CLI | ✅ Var | ✅ İnteraktif + betiklenebilir |
| Dil | C (C99) | C# (.NET 10) |
| Platform | Windows | Windows |
| Lisans | Apache 2.0 | GPL v2 |

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

Bu proje **GNU General Public License v2.0** altında lisanslanmıştır.

> Limbo, LGPL v3 / GPL v2 altında çifte lisanslı [WinDivert](https://github.com/basil00/WinDivert) kullandığından, lisans uyumluluğunu korumak için Limbo **GPL v2** altında dağıtılır.

Tam lisans metni için [LICENSE](LICENSE) dosyasına bakın.

---

<div align="center">

**⭐ Faydalı bulduysanız bu repoya yıldız verin!**

*Tasarım & Geliştirme: [Umit Korkmaz](https://github.com/umitkrkmz)*

</div>
