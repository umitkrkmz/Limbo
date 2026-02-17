# Limbo DPI Bypass Engine — Proje Raporu

**Tarih:** 2026-02-17
**Sürüm:** 0.3.0
**Platform:** Windows (.NET 10.0)

---

## 1. Proje Genel Bakış

**Limbo**, Deep Packet Inspection (DPI) tabanlı internet kısıtlamalarını aşmak için geliştirilmiş, açık kaynaklı bir bypass motorudur. Windows üzerinde WinDivert sürücüsü aracılığıyla ağ paketlerini düşük seviyede yakalar ve 7 farklı strateji + otomatik kalibrasyon ile hedef ISP veya güvenlik duvarı DPI sistemlerini atlatır.

### Temel Özellikler
- 7 bağımsız DPI bypass stratejisi (+ Adaptive mod)
- Alan adı bazlı wildcard profil sistemi
- **Radar Sistemi**: Engelli alanları otomatik tespit & kara listeye alma
- ISP davranış analizi (RST enjeksiyonu, sessiz düşmeler, DNS zehirlenmesi tespiti)
- DNS over HTTPS (DoH) desteği: Cloudflare, Google, Quad9
- Bant genişliği izleme (gerçek zamanlı)
- SIMD (AVX2) hızlandırmalı paket ayrıştırma
- ECH (Encrypted Client Hello) ile tam SNI şifreleme
- Hem WPF masaüstü arayüzü hem CLI uygulama

---

## 2. Proje Yapısı

```
Limbo/
├── src/
│   ├── Limbo.Core/            # Ana iş mantığı (30+ dosya)
│   ├── Limbo.Driver/          # WinDivert P/Invoke sarmalayıcı
│   ├── Limbo.Console/         # CLI uygulaması
│   └── Limbo.GUI/             # WPF masaüstü uygulaması
├── tests/
│   └── Limbo.Tests/           # xUnit birim testleri
├── .gitignore
├── LICENSE
└── REPORT.md
```

---

## 3. Bileşen Detayları

### 3.1 Limbo.Core

Ana kütüphane. Tüm bypass mantığı burada yer alır.

#### Engine Katmanı
| Sınıf | Açıklama |
|---|---|
| `LimboEngine` | Ana orkestrasyon motoru. Paket akışını yönetir, istatistik toplar. |
| `BypassEngine` | Strateji fabrikası. Hangi stratejinin çalıştırılacağını belirler. |
| `CalibrationEngine` | Bir alan adı için en iyi bypass metodunu otomatik bulur. |

#### Bypass Stratejileri
| Strateji | Öncelik | Protokol | Mekanizma |
|---|---|---|---|
| `EchBypassStrategy` | 200 | TCP | SNI'yi tamamen şifreler (ECH) |
| `QuicBypassStrategy` | 150 | UDP | QUIC Initial'e sahte datagram enjekte eder |
| `TcpFragmentationStrategy` | 100 | TCP | TLS ClientHello'yu küçük TCP segmentlerine böler |
| `SmartFragmentationStrategy` | 90 | TCP | Tam SNI ofsetinde ayırır (SIMD destekli) |
| `DesyncStrategy` | 80 | TCP | Paketleri sıra dışı gönderir |
| `FakeTtlStrategy` | 70 | TCP | DPI'ya ulaşıp sunucuya erişemeyen sahte paket gönderir |
| `UdpFakeStrategy` | 60 | UDP | UDP QUIC/ses trafiği için sahte paket tekniği |

#### Tespit & Analiz
| Sınıf | Açıklama |
|---|---|
| `PacketInspector` | IPv4/IPv6, TCP/UDP, TLS/HTTP ayrıştırma. SNI çıkarma. |
| `SimdPacketParser` | AVX2 ile 32 byte'lık SIMD hızlandırmalı SNI arama |
| `QuicInspector` | QUIC Initial paketi tespiti ve SNI çıkarma |
| `IspBehaviorAnalyzer` | RST enjeksiyonu, timeout, DNS zehirlenmesi analizi |
| `ConnectionTracker` | Sessiz düşmeleri (blackhole) tespit eder |
| `BlacklistManager` | Radar: engelli alanları dosyaya yazar/yükler |

#### DNS & Şifreleme
| Sınıf | Açıklama |
|---|---|
| `DoHResolver` | DNS over HTTPS (A + AAAA paralel sorgu, IPv6 öncelikli) |
| `DnsCache` | TTL'li, thread-safe bellek içi DNS önbelleği |
| `EchConfig` | ECH yapılandırma kaydı (HPKE X25519 + AES-128-GCM) |
| `EchConfigFetcher` | DNS HTTPS kaydından ECH yapılandırmasını getirir |
| `EchClientHelloBuilder` | ECH sarmalı TLS ClientHello oluşturur |
| `HpkeWrapper` | X25519 + HKDF-SHA256 + AES-128-GCM HPKE uygulaması |
| `ChaCha20Crypto` | ChaCha20-Poly1305 AEAD (tünel şifreleme için) |

#### İzleme & Loglama
| Sınıf | Açıklama |
|---|---|
| `BandwidthMonitor` | Kayan pencere ile gerçek zamanlı bant genişliği hesabı |
| `LimboLogger` | Yapılandırılmış log, alan adı filtresi, JSON dışa aktarma |

#### Yapılandırma & Profiller
| Sınıf | Açıklama |
|---|---|
| `LimboConfig` | Ana yapılandırma (filtre, DNS, temalar, sistem tepsisi) |
| `ProfileConfig` | Profil yapılandırması (alan adı + metod + parametreler) |
| `DomainProfile` | Runtime profil nesnesi (wildcard, doğrulama meta verisi) |
| `ProfileManager` | Wildcard destekli profil eşleştirme ve dosya persistansı |
| `ConfigManager` | JSON bazlı yapılandırma yönetimi |

---

### 3.2 Limbo.Driver

WinDivert 2.2 ile native Windows ağ sürücüsü entegrasyonu.

| Sınıf | Açıklama |
|---|---|
| `IPacketDriver` | Paket yakalama soyutlama arayüzü |
| `WinDivertDriver` | WinDivert uygulaması. Kuyruk boyutu: 16K paket, 32MB buffer |
| `WinDivertNative` | P/Invoke bildirimleri (80 byte explicit layout struct) |

**Kritik Düzeltme:** `WinDivertAddress` struct'ında Outbound bayrağı bit 16 değil bit 17'de (`flags >> 17 & 1`).

---

### 3.3 Limbo.Console

CLI uygulaması. GUI ile tam özellik paritesi.

#### Komutlar
| Komut | Açıklama |
|---|---|
| `run` | Bypass motorunu başlatır (varsayılan) |
| `calibrate <domain>` | En iyi bypass metodunu otomatik bulur |
| `test <domain>` | DNS, DoH, TCP:443, TCP:80 bağlantı testi |
| `add-profile <domain> <method> [opts]` | Profil ekler (`--frag`, `--ttl`, `--split`, `--offset`, `--ech-name`, `--quic-size`) |
| `remove-profile <domain>` | Profil siler |
| `radar` | Radar ile tespit edilen engelli alanları listeler |
| `radar-import <file>` | Kara liste dosyasını içe aktarır |
| `radar-export <file>` | Kara listeyi dosyaya aktarır |
| `radar-clear` | Radar kara listesini temizler |

#### Run Modu Özellikleri (v0.3)
- Radar sistemi entegrasyonu (BlacklistManager + ConnectionTracker + IspBehaviorAnalyzer)
- BandwidthMonitor: gerçek zamanlı ↓ indirme / ↑ yükleme hızı
- Tüm profil parametreleri desteği (SplitPosition, SplitOffset, FakeTtl, EchPublicName, FakePacketSize)
- İnbound paket takibi (sessiz düşme tespiti)
- 1 saniyelik istatistik güncelleme timer'ı
- Final istatistiklerinde bant genişliği özeti

---

### 3.4 Limbo.GUI (WPF)

Dark/Light tema destekli WPF masaüstü uygulaması.

#### Özellikler
- Profil listesi (ekle/düzenle/sil/içe aktar/dışa aktar)
- AddProfileDialog: metoda göre dinamik parametre gösterimi
- Gerçek zamanlı istatistikler (paket, eşleşme, değişiklik, bant genişliği)
- Renkli aktivite logu (RichTextBox, 1000 satır sınırı)
- Radar sistemi: auto-blacklist import/export
- Sistem tepsisi: minimize, gizle, sağ tık menüsü
- Tema: System / Light / Dark (Windows registry ile otomatik tespit)
- Bağlantı testi butonu
- Kalibrasyon dialog

#### XAML Dosyaları
- `MainWindow.xaml` - Ana pencere
- `AddProfileDialog.xaml` - Profil ekleme/düzenleme diyalogu
- `Themes/DarkTheme.xaml` - Koyu tema kaynakları
- `Themes/LightTheme.xaml` - Açık tema kaynakları
- `TrayIcon.xaml` - Sistem tepsisi kaynakları

---

## 4. Radar Sistemi (Otomatik Engel Tespiti)

Radar, ISP'nin engellediği alan adlarını otomatik tespit edip bypass profili oluşturan akıllı sistemdir.

```
Paket Analizi
    ↓
IspBehaviorAnalyzer                    ConnectionTracker
 (RST enjeksiyon tespiti)    +          (3s timeout → sessiz düşme)
    ↓                                         ↓
    └──────────────────────┬───────────────────┘
                           ↓
                  BlacklistManager
                 (Auto-Blacklist.txt)
                           ↓
               OnNewBlacklistedDomain event
                           ↓
              ProfileManager.SaveProfile()
              (Adaptive profil oluşturulur)
                           ↓
                GUI/CLI güncel liste yansıtılır
```

**Depolama:** `%LOCALAPPDATA%\Limbo\Auto-Blacklist.txt`

---

## 5. Paket İşleme Akışı

```
WinDivert (kernel) → WinDivertDriver.Receive()
    ↓
PacketInspector.Analyze()
  [IPv4/IPv6, TCP/UDP, TLS/HTTP, SNI/Host]
    ↓
Gelen (inbound)?
  → ConnectionTracker.RegisterInbound()  (sessiz düşme takibi)
  → Doğrudan gönder
    ↓
Giden (outbound)?
  → ConnectionTracker.TrackOutbound()
  → ProfileManager.GetProfile(domain)
  → BypassEngine.ProcessPacket(packet, context, profile)
      ↓
      Strateji.Process()  [önceliğe göre seçilir]
          ↓
          [1..N] PacketResult üretir
              ↓
  WinDivertDriver.Send() × N
```

---

## 6. Birim Testler

| Test Dosyası | Kapsam |
|---|---|
| `PacketInspectorTests.cs` | TLS ClientHello tespiti, HTTP Host çıkarma, TCP analizi, kısa paket |
| `BypassStrategyTests.cs` | TcpFragmentation, PacketResult oluşturma, inbound filtresi |
| `ProfileManagerTests.cs` | Tam eşleşme, wildcard eşleşme, varsayılan geri dönüş, öncelik |
| `DoHResolverTests.cs` | DNS önbellek set/get/expire, çoklu girdi, geçersiz alan adı, önbellek bypass |
| `BlacklistManagerTests.cs` | Ekleme, tekrar engeli, event tespiti, IP filtresi, export/import, persistans |
| `BandwidthMonitorTests.cs` | Download/upload toplamı, sıfırlama, oran hesabı, peak takibi, format |
| `IspBehaviorAnalyzerTests.cs` | RST analizi, timeout analizi, event tetikleme, alan filtrelemesi, DNS tespiti |

**Framework:** xUnit
**Toplam Test Sayısı:** ~40 test

---

## 7. Performans Özellikleri

| Özellik | Değer / Açıklama |
|---|---|
| WinDivert kuyruk | 16.384 paket, 32 MB buffer |
| SIMD (AVX2) | 32 byte blok halinde SNI arama |
| DNS önbelleği | Thread-safe ConcurrentDictionary, TTL bazlı |
| DoH paralel sorgu | A + AAAA eşzamanlı, IPv6 öncelikli |
| ECH yapılandırma önbelleği | 1 saat TTL |
| İstatistik sayaçları | Interlocked (lock-free) |
| Bant genişliği penceresi | 2 saniyelik kayan pencere |
| Bağlantı timeout tespiti | 3 saniyelik tarayıcı döngüsü |

---

## 8. Şifreleme & Güvenlik

| Bileşen | Algoritma | Kullanım |
|---|---|---|
| ECH | DHKEM(X25519) + HKDF-SHA256 + AES-128-GCM | SNI şifreleme |
| Tünel | ChaCha20-Poly1305 | Paket şifreleme (gelecek) |
| Anahtar türetme | PBKDF2 (100K iterasyon) | Şifre tabanlı |
| Anahtar değişimi | X25519 (ECDH) | Bouncy Castle |
| Sabit zamanlı karşılaştırma | `SecureEquals()` | Zamanlama saldırısı önleme |
| Bellek temizleme | `Array.Clear()` | Anahtar disposal |

---

## 9. Yapılandırma

**Konum:** `%LOCALAPPDATA%\Limbo\config.json`

```json
{
  "filter": "tcp or udp",
  "verbose": false,
  "dns": {
    "useDoH": false,
    "doHServer": "https://1.1.1.1/dns-query"
  },
  "theme": "System",
  "minimizeToTray": true,
  "closeToTray": true,
  "profiles": [
    { "domain": "*.discord.com", "method": "TcpFragmentation", "fragmentSize": 2 }
  ]
}
```

**Profil dosyaları:** `%LOCALAPPDATA%\Limbo\profiles\*.json`
**Radar kara listesi:** `%LOCALAPPDATA%\Limbo\Auto-Blacklist.txt`

---

## 10. Bağımlılıklar

| Paket | Proje | Kullanım |
|---|---|---|
| `SharpPcap` / WinDivert | Limbo.Driver | Paket yakalama |
| `BouncyCastle.Cryptography` | Limbo.Core | X25519 ECDH |
| `Hardcodet.NotifyIcon.Wpf` | Limbo.GUI | Sistem tepsisi |
| xUnit | Limbo.Tests | Birim testler |

---

## 11. Geliştirme Özeti (Bu Oturum)

### Limbo.Console Güncellemeleri (v0.2 → v0.3)
- **Radar sistemi entegrasyonu:** BlacklistManager + ConnectionTracker + IspBehaviorAnalyzer
- **BandwidthMonitor:** Canlı ↓/↑ istatistikleri `run` komutu ekranında
- **Yeni komutlar:** `radar`, `radar-import`, `radar-export`, `radar-clear`
- **Geliştirilmiş `add-profile`:** `--frag`, `--ttl`, `--split`, `--offset`, `--ech-name`, `--quic-size` parametreleri
- **Geliştirilmiş `test`:** Latency ölçümü, IP listesi, DNS tamper check
- **Geliştirilmiş `list-profiles`:** Tüm profil parametrelerini gösterir
- **Geliştirilmiş `--show-config`:** Tüm config alanları gösterilir
- **İnbound paket takibi:** Sessiz düşme tespiti için ConnectionTracker entegrasyonu
- **Tüm profil parametreleri:** `LoadProfilesFromConfig` helper metodu eklendi
- **`--list-methods`:** Öncelik tablosu eklendi

### Limbo.Core Güncellemeleri
- **`BlacklistManager`:** Test edilebilir `filePath` overload constructor eklendi

### Test Güncellemeleri
- `BlacklistManagerTests.cs` (8 test): Ekleme, duplicate, event, IP filtresi, export/import, persistans
- `BandwidthMonitorTests.cs` (8 test): Download/upload, sıfırlama, oran, peak, format
- `IspBehaviorAnalyzerTests.cs` (8 test): RST analizi, timeout, event, alan filtresi, DNS, istatistik

### Proje Altyapısı
- `.gitignore`: .NET/C#/WPF/WinDivert/kullanıcı verisi kuralları
- `LICENSE`: MIT lisansı

---

## 12. Bilinen Sınırlamalar & Gelecek Çalışmalar

| Konu | Durum | Not |
|---|---|---|
| DNS zehirlenmesi tam ayrıştırma | Eksik | `IspBehaviorAnalyzer.IsDnsPoisoned()` stub |
| ChaCha20 tünel | Tasarlandı | Henüz üst katman bağlanmadı |
| IPv6 DoH önbelleği | Çift kayıt | `domain:AAAA` ve `domain` anahtarı çakışabiliyor |
| Kalibrasyon GUI | Taslak | `Calibrate_Click` placeholder mesaj veriyor |
| Test radar-clear | Yok | Dosya silme admin bağımlı |
| Linux desteği | Yok | WinDivert Windows'a özgü |
| ISP fingerprint DB | Boş | `KnownIspPatterns` dictionary dolu değil |

---

*Limbo v0.3 — Rapor otomatik oluşturulmuştur.*
