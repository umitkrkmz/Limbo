using Limbo.Core.Config;

namespace Limbo.Console.Interactive;

/// <summary>
/// Minimal key-based translator for the interactive menu screens. English is
/// the base/default language (matching the rest of the codebase); Turkish is
/// an optional runtime setting under General Settings. Command-line/scripting
/// output (Commands/*.cs, --help) intentionally stays English-only.
/// </summary>
internal static class Loc
{
    public static Language Current { get; set; } = Language.English;

    public static string T(string key, params object[] args)
    {
        if (!Map.TryGetValue(key, out var pair))
            return key;

        var text = Current == Language.Turkish ? pair.Tr : pair.En;
        return args.Length == 0 ? text : string.Format(text, args);
    }

    private static readonly Dictionary<string, (string En, string Tr)> Map = new()
    {
        ["press_enter"] = ("Press Enter to continue...", "Devam etmek için Enter'a basın..."),
        ["back"] = ("« Back", "« Geri"),

        // Main menu
        ["main_menu.subtitle"] = ("Main Menu", "Ana Menü"),
        ["main_menu.prompt"] = ("What would you like to do?", "Ne yapmak istersiniz?"),
        ["main_menu.start_engine"] = ("Start Engine", "Motoru Başlat"),
        ["main_menu.profiles"] = ("Profile Management", "Profil Yönetimi"),
        ["main_menu.dns"] = ("DNS Settings", "DNS Ayarları"),
        ["main_menu.radar"] = ("Adaptive Engine (Radar)", "Adaptif Motor (Radar)"),
        ["main_menu.calibrate"] = ("Calibration", "Kalibrasyon"),
        ["main_menu.test"] = ("Connection Test", "Bağlantı Testi"),
        ["main_menu.settings"] = ("General Settings", "Genel Ayarlar"),
        ["main_menu.network_analysis"] = ("Network Analysis", "Ağ Analizi"),
        ["main_menu.about"] = ("About", "Hakkında"),
        ["main_menu.exit"] = ("Exit", "Çıkış"),

        // About + update check
        ["about.subtitle"] = ("About", "Hakkında"),
        ["about.version"] = ("Version", "Sürüm"),
        ["about.license"] = ("License", "Lisans"),
        ["about.source"] = ("Source code", "Kaynak kod"),
        ["about.third_party"] = ("Bundled components", "Paketteki bileşenler"),
        ["about.third_party_value"] = ("WinDivert, Spectre.Console, BouncyCastle, .NET runtime - see THIRD-PARTY-NOTICES.txt", "WinDivert, Spectre.Console, BouncyCastle, .NET runtime - bkz. THIRD-PARTY-NOTICES.txt"),
        ["about.updates"] = ("Updates", "Güncellemeler"),
        ["about.open_release"] = ("Open the download page for {0}", "{0} indirme sayfasını aç"),
        ["about.open_repo"] = ("Open the GitHub page", "GitHub sayfasını aç"),
        ["about.opened"] = ("Opened in your browser: {0}", "Tarayıcıda açıldı: {0}"),
        ["about.open_failed"] = ("Couldn't open a browser - the address is: {0}", "Tarayıcı açılamadı - adres: {0}"),
        ["update.disabled"] = ("Off (can be turned on in General Settings)", "Kapalı (Genel Ayarlar'dan açılabilir)"),
        ["update.checking"] = ("Checking...", "Kontrol ediliyor..."),
        ["update.up_to_date"] = ("Up to date", "Güncel"),
        ["update.available"] = ("New version available: {0}", "Yeni sürüm var: {0}"),
        ["update.failed"] = ("Couldn't check (offline or GitHub unreachable)", "Kontrol edilemedi (internet yok ya da GitHub'a ulaşılamadı)"),
        ["status.update"] = ("Update", "Güncelleme"),
        ["status.update_value"] = ("{0} available - see About", "{0} yayınlandı - bkz. Hakkında"),

        // Status panel
        ["status.admin"] = ("Administrator", "Yönetici"),
        ["status.admin_yes"] = ("✓ Yes", "✓ Evet"),
        ["status.admin_no"] = ("✗ No", "✗ Hayır"),
        ["status.interface"] = ("Network Interface", "Ağ Arayüzü"),
        ["status.not_found"] = ("(not found)", "(bulunamadı)"),
        ["status.system_dns"] = ("System DNS", "Sistem DNS"),
        ["status.radar_engine"] = ("Adaptive Engine", "Adaptif Motor"),
        ["status.on"] = ("On", "Açık"),
        ["status.off"] = ("Off", "Kapalı"),
        ["status.doh"] = ("DNS over HTTPS", "DNS over HTTPS"),
        ["status.profile_count"] = ("Profile Count", "Profil Sayısı"),
        ["status.radar_count"] = ("Radar Detections", "Radar Tespiti"),

        // Calibration
        ["calibrate.subtitle"] = ("Calibration", "Kalibrasyon"),
        ["calibrate.domain_prompt"] = ("Domain to calibrate:", "Kalibre edilecek domain:"),
        ["calibrate.port_prompt"] = ("Port:", "Port:"),
        ["calibrate.save_confirm"] = ("Create a profile for *.{0}?", "*.{0} için profil oluşturulsun mu?"),

        // Connection test
        ["test.subtitle"] = ("Connection Test", "Bağlantı Testi"),
        ["test.domain_prompt"] = ("Domain to test:", "Test edilecek domain:"),

        // General settings
        ["settings.subtitle"] = ("General Settings", "Genel Ayarlar"),
        ["settings.filter"] = ("Filter", "Filter"),
        ["settings.verbose"] = ("Verbose", "Verbose"),
        ["settings.log_file"] = ("Log File", "Log Dosyası"),
        ["settings.log_file_none"] = ("(none)", "(yok)"),
        ["settings.config_file"] = ("Config File", "Config Dosyası"),
        ["settings.language"] = ("Language", "Dil"),
        ["settings.update_check"] = ("Update check", "Güncelleme kontrolü"),
        ["settings.toggle_update_check"] = ("Toggle Update Check", "Güncelleme Kontrolünü Aç/Kapa"),
        ["settings.prompt"] = ("What would you like to do?", "Ne yapmak istersiniz?"),
        ["settings.toggle_verbose"] = ("Toggle Verbose", "Verbose Aç/Kapa"),
        ["settings.set_log_file"] = ("Set Log File", "Log Dosyası Ayarla"),
        ["settings.change_language"] = ("Change Language", "Dili Değiştir"),
        ["settings.log_file_prompt"] = ("Log file path (empty = off):", "Log dosyası yolu (boş = kapat):"),
        ["settings.language_prompt"] = ("Choose a language:", "Dil seçin:"),
        ["settings.language_saved"] = ("Language updated.", "Dil güncellendi."),
        ["on"] = ("on", "açık"),
        ["off"] = ("off", "kapalı"),

        // Profile management
        ["profiles.subtitle"] = ("Profile Management", "Profil Yönetimi"),
        ["profiles.prompt"] = ("What would you like to do?", "Ne yapmak istersiniz?"),
        ["profiles.list"] = ("List Profiles", "Profilleri Listele"),
        ["profiles.add"] = ("Add Profile", "Profil Ekle"),
        ["profiles.edit"] = ("Edit Profile", "Profil Düzenle"),
        ["profiles.delete"] = ("Delete Profile", "Profil Sil"),
        ["profiles.list_subtitle"] = ("Profiles", "Profiller"),
        ["profiles.add_subtitle"] = ("Add Profile", "Profil Ekle"),
        ["profiles.edit_subtitle"] = ("Edit Profile: {0}", "Profil Düzenle: {0}"),
        ["profiles.none_to_edit"] = ("No profiles to edit.", "Düzenlenecek profil yok."),
        ["profiles.none_to_delete"] = ("No profiles to delete.", "Silinecek profil yok."),
        ["profiles.select_to_edit"] = ("Select a profile to edit:", "Düzenlenecek profili seçin:"),
        ["profiles.select_to_delete"] = ("Select a profile to delete:", "Silinecek profili seçin:"),
        ["profiles.confirm_delete"] = ("Delete profile '{0}'?", "'{0}' profilini silmek istediğinize emin misiniz?"),
        ["profiles.deleted"] = ("Deleted:", "Silindi:"),
        ["profiles.domain_prompt"] = ("Domain (e.g. [grey]*.discord.com[/]):", "Domain (örn. [grey]*.discord.com[/]):"),
        ["profiles.domain_empty_error"] = ("Domain cannot be empty", "Domain boş olamaz"),
        ["profiles.method_prompt"] = ("Select bypass method:", "Bypass yöntemi seçin:"),
        ["profiles.use_adaptive_prompt"] = ("Use adaptive (auto-tuned) parameters?", "Adaptif (otomatik ayarlanan) parametreler kullanılsın mı?"),
        ["profiles.saved"] = ("Profile saved:", "Profil kaydedildi:"),

        // Method parameters (shared by profile wizard + radar default-method screen)
        ["param.fragment_size"] = ("Fragment size:", "Fragment boyutu:"),
        ["param.split_position"] = ("Split position:", "Split pozisyonu:"),
        ["param.fake_ttl"] = ("Fake TTL value:", "Sahte TTL değeri:"),
        ["param.ech_name"] = ("ECH public name (empty = auto):", "ECH public name (boş = otomatik):"),
        ["param.quic_size"] = ("Fake packet size:", "Sahte paket boyutu:"),

        // DNS settings
        ["dns.subtitle"] = ("DNS Settings", "DNS Ayarları"),
        ["dns.interface"] = ("Active Interface", "Aktif Arayüz"),
        ["dns.system_dns"] = ("System DNS", "Sistem DNS"),
        ["dns.not_found"] = ("(not found)", "(bulunamadı)"),
        ["dns.doh"] = ("DNS over HTTPS", "DNS over HTTPS"),
        ["dns.server"] = ("DoH Server", "DoH Sunucusu"),
        ["dns.doh_note"] = ("With DoH on, while the engine runs every app's DNS queries (IPv4 and IPv6) are answered over this encrypted server, so the ISP can't poison them. System DNS settings aren't changed; if the server is unreachable, normal DNS is used.", "DoH açıkken motor çalıştığı sürece tüm uygulamaların DNS sorguları (IPv4 ve IPv6) bu şifreli sunucu üzerinden cevaplanır, İSS zehirleyemez. Sistem DNS ayarlarına dokunulmaz; sunucuya ulaşılamazsa normal DNS kullanılır."),
        ["dns.prompt"] = ("What would you like to do?", "Ne yapmak istersiniz?"),
        ["dns.toggle"] = ("Toggle DoH", "DoH Aç/Kapa"),
        ["dns.pick_provider"] = ("Choose Provider", "Sağlayıcı Seç"),
        ["dns.test_now"] = ("Test Now", "Şimdi Test Et"),
        ["dns.enabled"] = ("DoH enabled.", "DoH açıldı."),
        ["dns.disabled"] = ("DoH disabled.", "DoH kapatıldı."),
        ["dns.provider_prompt"] = ("Choose a DoH provider:", "DoH sağlayıcısı seçin:"),
        ["dns.custom_url_prompt"] = ("Enter DoH URL:", "DoH URL girin:"),
        ["dns.custom_option"] = ("Custom URL...", "Özel URL gir..."),
        ["dns.saved"] = ("Saved:", "Kaydedildi:"),
        ["dns.test_domain_prompt"] = ("Domain to test:", "Test edilecek domain:"),

        // Radar / adaptive engine
        ["radar.subtitle"] = ("Adaptive Engine (Radar)", "Adaptif Motor (Radar)"),
        ["radar.status"] = ("Status:", "Durum:"),
        ["radar.detected_count"] = ("Detected domains:", "Tespit edilen domain sayısı:"),
        ["radar.prompt"] = ("What would you like to do?", "Ne yapmak istersiniz?"),
        ["radar.turn_off"] = ("Turn Off Adaptive Engine", "Adaptif Motoru Kapat"),
        ["radar.turn_on"] = ("Turn On Adaptive Engine", "Adaptif Motoru Aç"),
        ["radar.list"] = ("List Detected Domains", "Tespit Edilenleri Listele"),
        ["radar.default_method"] = ("Change Default Method", "Varsayılan Yöntemi Değiştir"),
        ["radar.import"] = ("Import", "İçe Aktar"),
        ["radar.export"] = ("Export", "Dışa Aktar"),
        ["radar.clear"] = ("Clear", "Temizle"),
        ["radar.turned_off"] = ("Adaptive engine turned off. Only manual profiles will be used.", "Adaptif motor kapatıldı. Sadece manuel profiller kullanılacak."),
        ["radar.turned_on"] = ("Adaptive engine turned on.", "Adaptif motor açıldı."),
        ["radar.list_subtitle"] = ("Radar - Detected Domains", "Radar - Tespit Edilenler"),
        ["radar.import_prompt"] = ("File path to import:", "İçe aktarılacak dosya yolu:"),
        ["radar.export_prompt"] = ("File path to export:", "Dışa aktarılacak dosya yolu:"),
        ["radar.default_method_subtitle"] = ("Default Method for Auto-Detected Domains", "Otomatik Tespit Edilen Domainler için Varsayılan Yöntem"),
        ["radar.current_default"] = ("Current default: {0}", "Şu anki varsayılan: {0}"),
        ["radar.default_method_saved"] = ("Default method updated.", "Varsayılan yöntem güncellendi."),

        // Network analysis
        ["network.subtitle"] = ("Network Analysis", "Ağ Analizi"),
        ["network.fetching"] = ("Looking up your public IP/ISP...", "Genel IP/ISS bilgisi sorgulanıyor..."),
        ["network.public_ip"] = ("Public IP", "Genel IP"),
        ["network.isp"] = ("ISP / Organization", "ISS / Kuruluş"),
        ["network.location"] = ("Location", "Konum"),
        ["network.not_available"] = ("(not available - no network or lookup service unreachable)", "(alınamadı - ağ yok ya da sorgu servisine ulaşılamadı)"),
        ["network.local_interface"] = ("Local Interface", "Yerel Arayüz"),
        ["network.local_dns"] = ("Local DNS Servers", "Yerel DNS Sunucuları"),
        ["network.domain_prompt"] = ("Domain to scan (empty = skip):", "Taranacak domain (boş = atla):"),
        ["network.intercept_busy"] = ("Another Limbo instance is already intercepting traffic (run, calibration or network scan). Stop it first - the scan would see packets that copy is rewriting.", "Başka bir Limbo kopyası zaten trafiği yakalıyor (çalıştırma, kalibrasyon ya da ağ taraması). Önce onu durdurun - aksi halde tarama o kopyanın değiştirdiği paketleri görür."),
        ["network.requires_admin_note"] = ("Administrator privileges required for the live behavior scan (RST injection / silent drop detection) - showing network identity only.", "Canlı davranış taraması (RST enjeksiyonu / sessiz düşürme tespiti) için yönetici yetkisi gerekiyor - sadece ağ kimliği gösteriliyor."),
        ["network.scanning"] = ("Scanning...", "Taranıyor..."),
        ["network.dns_tamper_yes"] = ("DNS tampering suspected (system DNS differs from DoH)", "DNS tamper şüphesi (sistem DNS, DoH'tan farklı)"),
        ["network.dns_tamper_no"] = ("DNS looks consistent with DoH", "DNS, DoH ile tutarlı görünüyor"),
        ["network.outcome"] = ("Result", "Sonuç"),
        ["network.outcome.reachable"] = ("Reachable - no interference detected", "Ulaşılabilir - müdahale tespit edilmedi"),
        ["network.outcome.rst"] = ("RST injection detected", "RST enjeksiyonu tespit edildi"),
        ["network.outcome.silentdrop"] = ("Silently dropped (no response at all)", "Sessizce düşürüldü (hiç yanıt yok)"),
        ["network.outcome.unreachable"] = ("Unreachable (reason unclear)", "Ulaşılamıyor (sebep belirsiz)"),
        ["network.outcome.dnsfail"] = ("Could not resolve domain", "Domain çözümlenemedi"),
        ["network.outcome.noadmin"] = ("Skipped (requires Administrator)", "Atlandı (Yönetici yetkisi gerekiyor)")
    };
}
