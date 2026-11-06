# -*- coding: utf-8 -*-
"""Handcrafted natural EN/TR descriptions for tweaks, presets, tools, net, tm."""

NATURAL_TWEAKS: dict[str, dict[str, str]] = {
    "tweak.gamemode.desc": {
        "en": "Tells Windows you’re gaming so background updates and indexing step back and the game gets more focus. Harmless and recommended for gamers.",
        "tr": "Windows’a oyun oynadığını söyler; arka plan güncelleme ve indeksleme geriler, oyun biraz daha öne çıkar. Zararsızdır, oyuncuya önerilir.",
    },
    "tweak.gamebar.desc": {
        "en": "Turns off Game Bar’s always-ready background capture. Instant Win+Alt+G clips may stop working, but FPS usually improves — a favorite for competitive play.",
        "tr": "Game Bar’ın arka planda “kayıta hazır” kalmasını kapatır. Win+Alt+G anlık klipler bozulabilir ama FPS genelde artar; rekabetçi oyunda sık tercih edilir.",
    },
    "tweak.gamedvr.desc": {
        "en": "Policy switch that disables Game DVR / background recording more aggressively. Turn it on if you don’t capture clips.",
        "tr": "Game DVR / arka plan kaydını politikayla daha sert kapatır. Klip çekmiyorsan açman yeterli.",
    },
    "tweak.hags.desc": {
        "en": "Lets the GPU schedule frames with less CPU involvement (HAGS). Can lower latency or cause stutter on some driver/game combos — reboot after changing, and turn it off if things get worse.",
        "tr": "GPU’nun kareleri CPU’ya daha az bağımlı planlamasını açar (HAGS). Gecikmeyi düşürebilir veya bazı sürücü/oyunlarda takılma yapabilir; değişince yeniden başlat, kötüleşirse kapat.",
    },
    "tweak.ultimateperf.desc": {
        "en": "Unlocks Windows’s hidden Ultimate Performance power plan so CPU/GPU save less power. FPS gets more consistent; laptops drain faster and the PC may run hotter.",
        "tr": "Windows’un gizli Ultimate Performance güç planını açar; CPU/GPU daha az güç tasarrufu yapar. FPS daha tutarlı olur, laptoplarda batarya çabuk biter, PC ısınabilir.",
    },
    "tweak.nagle.desc": {
        "en": "Stops the network from bundling small packets before sending (TcpAckFrequency). Competitive online games may feel a bit snappier; rare networks might lose a little throughput.",
        "tr": "Ağın küçük paketleri biriktirip sonra göndermesini azaltır (TcpAckFrequency). Rekabetçi online oyunlarda gecikme biraz düşebilir; nadir ağlarda hız çok az etkilenir.",
    },
    "tweak.tcpnodelay.desc": {
        "en": "Same family as disabling Nagle — tells TCP to send immediately instead of waiting. Use it together with the Nagle tweak.",
        "tr": "Nagle kapatmayla aynı aile: TCP’ye beklemeden hemen gönder der. Nagle ayarıyla birlikte kullanılır.",
    },
    "tweak.fso.desc": {
        "en": "Pushes games toward classic exclusive fullscreen instead of borderless-window behavior. Helps alt-tab and input in some older or picky titles — not required for every game.",
        "tr": "Oyunları kenarlıksız pencere yerine klasik tam ekrana yaklaştırır. Bazı eski veya huysuz oyunlarda alt-tab ve girişi düzeltir; her oyun için şart değil.",
    },
    "tweak.mmcss.desc": {
        "en": "Raises GPU priority in Windows’s Games task profile so the graphics card gets more room while you play. Fine on most PCs (caution-level).",
        "tr": "Windows’un Oyunlar görev profilinde GPU önceliğini yükseltir; oyun açıkken ekran kartına daha fazla alan kalır. Çoğu PC’de sorunsuzdur (dikkat seviyesi).",
    },
    "tweak.mmcssprio.desc": {
        "en": "Raises the scheduling priority number for games so Windows treats them as more important work.",
        "tr": "Oyunların zamanlama öncelik sayısını yükseltir; Windows onları daha önemli iş gibi ele alır.",
    },
    "tweak.mmcsssched.desc": {
        "en": "Sets the games scheduling category to High. Works as a package with the other MMCSS tweaks.",
        "tr": "Oyunların zamanlama kategorisini Yüksek yapar. Diğer MMCSS ayarlarıyla birlikte bir paket gibi düşün.",
    },
    "tweak.bgapps.desc": {
        "en": "Limits Store (UWP) apps from quietly running in the background. Less mystery CPU/RAM use; some notifications and sync may lag.",
        "tr": "Store (UWP) uygulamalarının arka planda sessizce çalışmasını kısıtlar. Gizemli CPU/RAM kullanımı azalır; bazı bildirim ve senkronlar gecikebilir.",
    },
    "tweak.mouseaccel.desc": {
        "en": "Turns off Enhance pointer precision (mouse acceleration) so aim distance matches hand movement. Desktop mouse feel changes at first — strongly recommended for FPS.",
        "tr": "«Enhance pointer precision» (fare ivmesi) özelliğini kapatır; nişan mesafesi el hareketinle orantılı kalır. Masaüstünde fare ilk başta farklı gelir — FPS için şiddetle önerilir.",
    },
    "tweak.vbs.desc": {
        "en": "Turns off Memory Integrity / core isolation (HVCI). Some PCs gain more consistent input or FPS, but kernel security gets weaker. Needs a reboot.",
        "tr": "Bellek bütünlüğü / çekirdek yalıtımını (HVCI) kapatır. Bazı PC’lerde giriş veya FPS daha tutarlı olur ama çekirdek güvenliği zayıflar. Yeniden başlatma gerekir.",
    },
    "tweak.dynamictick.desc": {
        "en": "Sets disabledynamictick via bcdedit. May smooth DPC / frame times on some systems — not a miracle everywhere. Needs Administrator; reversible with bcdedit.",
        "tr": "bcdedit ile disabledynamictick ayarlar. Bazı sistemlerde DPC / kare sürelerini yumuşatabilir; her yerde mucize değil. Yönetici ister; bcdedit ile geri alınır.",
    },
    "tweak.hpet.desc": {
        "en": "Removes forced platform clock usage (bcdedit /deletevalue useplatformclock). Helps latency on some boards, hurts others — measure before keeping it. Needs Administrator.",
        "tr": "Zorunlu platform saati kullanımını kaldırır (bcdedit). Bazı anakartlarda gecikmeyi iyileştirir, bazılarında bozar — ölçmeden kalıcı yapma. Yönetici ister.",
    },
    "tweak.telemetry.desc": {
        "en": "Sets Windows diagnostic data sent to Microsoft as low as possible. Less “Windows is watching” traffic; Home edition may not reach absolute zero but still cuts a lot.",
        "tr": "Microsoft’a giden tanılama verisini olabildiğince düşürür. Daha az “Windows izliyor” trafiği; Home sürümünde sıfır garanti değil ama epey kısar.",
    },
    "tweak.adid.desc": {
        "en": "Turns off the advertising ID apps use to track you. Less personalized ads; games keep working.",
        "tr": "Uygulamaların seni reklam için izlediği reklam kimliğini kapatır. Kişiselleştirilmiş reklam azalır; oyunlar etkilenmez.",
    },
    "tweak.tailored.desc": {
        "en": "Stops tips, suggestions, and ads based on your diagnostic data — fewer “just for you” pop-ups.",
        "tr": "Tanılama verine göre ipucu, öneri ve reklamları keser; “sana özel” pencereler azalır.",
    },
    "tweak.cortana.desc": {
        "en": "Disables Cortana via policy (already weak on newer Windows). Less voice-assistant background activity.",
        "tr": "Cortana’yı politikayla kapatır (yeni Windows’ta zaten güçsüz). Sesli asistan arka plan işi azalır.",
    },
    "tweak.bing.desc": {
        "en": "Stops Start search from pulling Bing / web results so you only see what’s on your PC — faster and cleaner. A very popular tweak.",
        "tr": "Başlat aramasının Bing / web sonucu çekmesini keser; sadece PC’ndekiler çıkar — daha hızlı ve sade. Çok sevilen bir ayar.",
    },
    "tweak.location.desc": {
        "en": "Generally denies apps asking for your location. Better privacy; maps and weather may lose location features.",
        "tr": "Uygulamaların konum istemesini genel olarak reddeder. Gizlilik artar; harita ve hava durumu konum özelliklerini kaybedebilir.",
    },
    "tweak.activity.desc": {
        "en": "Stops sending “which app you opened when” history to the cloud / Timeline. Cross-device resume gets weaker.",
        "tr": "“Hangi uygulamayı ne zaman açtın” geçmişinin buluta / Zaman Çizelgesi’ne gitmesini keser. Cihazlar arası devam zayıflar.",
    },
    "tweak.visualperf.desc": {
        "en": "Tells Windows to prefer speed over polish — fewer shadows, smooth scrolls, and animations. The desktop looks flatter but feels lighter.",
        "tr": "Windows’a gösteriş yerine hız de; gölge, yumuşak kaydırma ve animasyonlar azalır. Masaüstü daha yalın ama daha hafif hissettirir.",
    },
    "tweak.animations.desc": {
        "en": "Turns off minimize / maximize window animations so windows appear instantly.",
        "tr": "Küçült / büyüt pencere animasyonlarını kapatır; pencereler anında gelir.",
    },
    "tweak.transparency.desc": {
        "en": "Turns off frosted-glass transparency on the taskbar and Start. Slightly more matte look, a bit less GPU/CPU on eye candy.",
        "tr": "Görev çubuğu ve Başlat’taki buzlu cam şeffaflığını kapatır. Biraz daha mat durur, süs için harcanan GPU/CPU azalır.",
    },
    "tweak.qos.desc": {
        "en": "Clears Windows’s default bandwidth reserve (often ~20%). In theory the full pipe is yours — harmless to try even if the gain is small.",
        "tr": "Windows’un varsayılan bant genişliği rezervini temizler (çoğu zaman ~%20). Teoride tüm hat senindir; kazanç küçük olsa da denemesi zararsız.",
    },
    "tweak.tcpauto.desc": {
        "en": "Sets TCP window autotuning to normal via netsh. Useful when downloads or the network behave oddly — a one-shot command.",
        "tr": "netsh ile TCP pencere otomatik ayarını normal yapar. İndirme veya ağ garipleştiğinde işe yarar; tek seferlik komut.",
    },
    "tweak.dnsflush.desc": {
        "en": "Clears the PC’s cached DNS answers. Handy when a site won’t load, goes to the wrong place, or acts weird after a VPN — harmless one-time cleanup.",
        "tr": "PC’nin önbellekteki DNS cevaplarını temizler. Site açılmayınca, yanlış yere gidince veya VPN sonrası garipleşince işe yarar; zararsız tek seferlik temizlik.",
    },
    "tweak.netbios.desc": {
        "en": "Disables NetBIOS on every NetBT interface. Less legacy broadcast chatter on modern networks; very old LAN sharing may be affected.",
        "tr": "Tüm NetBT arayüzlerinde NetBIOS’u kapatır. Modern ağlarda eski yayın gürültüsü azalır; çok eski LAN paylaşımları etkilenebilir.",
    },
    "tweak.deliveryopt.desc": {
        "en": "Stops Windows Update from sharing updates P2P with other PCs. Updates still come from Microsoft; your upload isn’t wasted — great on a data cap.",
        "tr": "Windows Update’in güncellemeleri P2P paylaşmasını keser. Güncelleme yine Microsoft’tan gelir; yükleme kotan boşa gitmez — kotası olanlar için iyi.",
    },
    "tweak.wupause.desc": {
        "en": "Fully stops the Windows Update service. No surprise updates, but also no security patches — OK for a short pause before a tournament or stream, then turn it back on. Needs “allow dangerous actions”.",
        "tr": "Windows Update servisini tamamen durdurur. Sürpriz güncelleme olmaz ama güvenlik yaması da gelmez — turnuva veya yayın öncesi kısa ara için OK, sonra aç. “Tehlikeli işlemlere izin ver” gerekir.",
    },
    "tweak.taskceip.desc": {
        "en": "Disables the Customer Experience Improvement scheduled task that sends usage summaries to Microsoft. One less telemetry job.",
        "tr": "Microsoft’a kullanım özeti gönderen CEIP zamanlanmış görevini kapatır. Bir telemetri işi eksilir.",
    },
    "tweak.taskusbceip.desc": {
        "en": "Disables the CEIP task that tracks USB device usage. Less telemetry; USB devices still work.",
        "tr": "USB kullanımını izleyen CEIP görevini kapatır. Telemetri azalır; USB cihazlar çalışmaya devam eder.",
    },
    "tweak.classicmenu.desc": {
        "en": "Restores the full classic right-click menu on Windows 11 instead of the compact one. Explorer restarts briefly when you toggle it.",
        "tr": "Windows 11’deki kısa menü yerine klasik sağ tık menüsünü getirir. Açıp kapatınca Explorer kısa süre yeniden başlar.",
    },
    "tweak.copilot.desc": {
        "en": "Turns off Windows Copilot via policy (taskbar / Win+C). Windows 11 only.",
        "tr": "Windows Copilot’u politikayla kapatır (görev çubuğu / Win+C). Sadece Windows 11.",
    },
    "tweak.recall.desc": {
        "en": "Disables Windows Recall / AI screen-data analysis via policy. Recommended for privacy on Windows 11.",
        "tr": "Windows Recall / yapay zekâ ekran verisi analizini politikayla kapatır. Windows 11’de gizlilik için önerilir.",
    },
    "tweak.startiris.desc": {
        "en": "Hides Start menu recommendations. Windows can show them again after a feature update. This does not mark the PC as an education device. If you turned this on in an older version, switch it off and on once to clear that mark. Explorer may restart.",
        "tr": "Başlat menüsü önerilerini gizler. Bir özellik güncellemesinden sonra öneriler geri gelebilir. Bu anahtar bilgisayarı eğitim cihazı olarak işaretlemez. Eski bir sürümde açtıysanız, eski işareti silmek için bir kez kapatıp yeniden açın. Explorer yeniden başlayabilir.",
    },
    "tweak.widgets.desc": {
        "en": "Disables taskbar Widgets and the News and interests feed. Less background chatter; Explorer may restart.",
        "tr": "Görev çubuğu Widget’larını ve Haberler ve ilgi alanları akışını kapatır. Arka plan gürültüsü azalır; Explorer yeniden başlayabilir.",
    },
    "tweak.inking.desc": {
        "en": "Stops sending keyboard / handwriting usage data to Microsoft for personalization. Suggestions get more generic; privacy improves.",
        "tr": "Klavye / el yazısı kullanım verisinin kişiselleştirme için Microsoft’a gitmesini keser. Öneriler daha genel olur; gizlilik artar.",
    },
    "tweak.feedbackfreq.desc": {
        "en": "Stops Windows “Give us feedback” survey pop-ups.",
        "tr": "Windows’un “Bize geri bildirim ver” anket pencerelerini keser.",
    },
    "tweak.onlinespeech.desc": {
        "en": "Stops sending voice data to the cloud for speech recognition. Online speech features turn off; local ones stay.",
        "tr": "Konuşma tanıma için ses verisinin buluta gitmesini keser. Çevrimiçi konuşma özellikleri kapanır; yerel olanlar kalır.",
    },
    "tweak.powerthrottle.desc": {
        "en": "Stops Windows from throttling CPU for background / inactive windows. Minimized helpers like Discord get throttled less while gaming; laptop battery may suffer.",
        "tr": "Windows’un arka plan / pasif pencerelerde CPU’yu kısmasını durdurur. Discord gibi alta alınmış yardımcılar oyundayken daha az kısıtlanır; laptop bataryası etkilenebilir.",
    },
    "tweak.sysresp.desc": {
        "en": "Sets the CPU reserve for background tasks (default ~20%) to zero so games and multimedia get more CPU time.",
        "tr": "Arka plan işleri için CPU rezervini (varsayılan ~%20) sıfıra çeker; oyun ve medya daha fazla CPU alır.",
    },
    "tweak.coreparking.desc": {
        "en": "Keeps all CPU cores awake on the current power plan (no core parking). Fewer lags on sudden load spikes; higher power use.",
        "tr": "Geçerli güç planında tüm çekirdekleri uyanık tutar (core parking yok). Ani yükte daha az takılma; güç tüketimi artar.",
    },
    "tweak.darkmode.desc": {
        "en": "Turns on dark theme for apps and the system UI.",
        "tr": "Uygulamalar ve sistem arayüzü için koyu temayı açar.",
    },
    "tweak.lockscreenads.desc": {
        "en": "Turns off Windows Spotlight ads and tip overlays on the lock screen so you get a plain static image.",
        "tr": "Kilit ekranındaki Windows Spotlight reklam ve ipucu katmanlarını kapatır; sade sabit bir görsel kalır.",
    },
    "tweak.showext.desc": {
        "en": "Shows file extensions and hidden files in Explorer — easier to spot fake “photo.jpg.exe” files. Explorer restarts.",
        "tr": "Explorer’da dosya uzantılarını ve gizli dosyaları gösterir; sahte “foto.jpg.exe” dosyalarını yakalamak kolaylaşır. Explorer yeniden başlar.",
    },
    "tweak.taskbarleft.desc": {
        "en": "Moves Windows 11 taskbar icons back to the classic left alignment. Explorer restarts.",
        "tr": "Windows 11 görev çubuğu simgelerini klasik sola hizaya alır. Explorer yeniden başlar.",
    },
    "tweak.hidechat.desc": {
        "en": "Hides the Teams / Chat icon on the taskbar. Explorer restarts.",
        "tr": "Görev çubuğundaki Teams / Sohbet simgesini gizler. Explorer yeniden başlar.",
    },
    "tweak.searchicon.desc": {
        "en": "Shrinks the wide taskbar search box to a small icon. Explorer restarts.",
        "tr": "Geniş görev çubuğu arama kutusunu küçük bir simgeye indirir. Explorer yeniden başlar.",
    },
    "tweak.hidetaskview.desc": {
        "en": "Hides the Task View button on the taskbar without uninstalling anything. Explorer restarts.",
        "tr": "Görev çubuğundaki Görev Görünümü düğmesini gizler; bir şey kaldırılmaz. Explorer yeniden başlar.",
    },
    "tweak.explorerthispc.desc": {
        "en": "Makes File Explorer open to This PC instead of Home.",
        "tr": "Dosya Gezgini’ni Ana sayfa yerine Bu Bilgisayar ile açtırır.",
    },
    "tweak.netthrottle.desc": {
        "en": "Removes Windows’s multimedia/game network packet processing limit. Under heavy online traffic, packet delay may drop.",
        "tr": "Windows’un medya/oyun ağ paket işleme sınırını kaldırır. Yoğun online trafikte paket gecikmesi düşebilir.",
    },
    "tweak.ipv6.desc": {
        "en": "Disables IPv6 system-wide. Some modern services (console crossplay, certain VPNs) may need IPv6 — don’t combine with Prefer IPv4. Use only if IPv6 is causing connectivity issues; reboot recommended.",
        "tr": "IPv6’yı sistem genelinde kapatır. Bazı modern servisler (konsol crossplay, bazı VPN’ler) IPv6 isteyebilir — IPv4’ü tercih et ile birlikte kullanma. Sadece IPv6 bağlantı sorunu yapıyorsa; yeniden başlatma önerilir.",
    },
    "tweak.nicpower.desc": {
        "en": "Stops Windows from sleeping network adapters to save power. Can reduce random micro-disconnects and lag spikes in online games.",
        "tr": "Windows’un güç için ağ kartını uyutmasını engeller. Online oyunda ara sıra kopma ve gecikme sıçramalarını azaltabilir.",
    },
    "tweak.hibernation.desc": {
        "en": "Turns off hibernation and deletes the large hiberfil.sys file. Frees disk space; you lose hibernate / hybrid sleep.",
        "tr": "Hazırda bekletmeyi kapatır ve büyük hiberfil.sys dosyasını siler. Disk açılır; hibernate / hybrid sleep kaybolur.",
    },
    "tweak.wpbt.desc": {
        "en": "Blocks OEM WPBT payloads from running at boot so some vendor bloat can’t reinstall itself. Rare OEM tools may depend on it.",
        "tr": "Açılışta OEM WPBT yüklerinin çalışmasını engeller; bazı üretici şişkinlikleri kendini yeniden kuramaz. Nadir OEM araçları buna bağlı olabilir.",
    },
    "tweak.endtask.desc": {
        "en": "Adds End task to the Windows 11 taskbar app right-click menu.",
        "tr": "Windows 11 görev çubuğu uygulama menüsüne Görevi sonlandır ekler.",
    },
    "tweak.devmeta.desc": {
        "en": "Stops Windows from downloading device companion metadata from the internet — less background network chatter.",
        "tr": "Windows’un internetten cihaz yardımcı meta verisi indirmesini keser; arka plan ağ gürültüsü azalır.",
    },
    "tweak.storesearch.desc": {
        "en": "Blocks access to the Microsoft Store local search database (WinUtil-style). Fragile — Store updates may reset it and Store search can break.",
        "tr": "Microsoft Store yerel arama veritabanına erişimi keser (WinUtil tarzı). Kırılgan — Store güncellemesi sıfırlayabilir, Store araması bozulabilir.",
    },
    "tweak.folderdiscovery.desc": {
        "en": "Stops Explorer from auto-guessing folder templates (clears Bags/BagMRU). Needs sign-out or reboot; undo only partly clears bags again.",
        "tr": "Explorer’ın klasör şablonlarını otomatik tahmin etmesini keser (Bags/BagMRU temizliği). Oturumu kapatma veya yeniden başlatma gerekir; geri alma kısmen yeniden temizler.",
    },
    "tweak.homegallery.desc": {
        "en": "Unpins Home and Gallery from the Explorer navigation pane on Windows 11. Use “Open Explorer to This PC” separately if you want that too. Explorer restarts.",
        "tr": "Windows 11 Explorer kenar çubuğundan Ana sayfa ve Galeri’yi kaldırır. Bu Bilgisayar ile açmayı ayrıca ayarla. Explorer yeniden başlar.",
    },
    "tweak.reservedstorage.desc": {
        "en": "Disables Windows Reserved Storage via DISM and can reclaim several GB. Updates may need more free space afterward.",
        "tr": "DISM ile Windows Ayrılmış Depolama’yı kapatır; birkaç GB geri gelebilir. Sonraki güncellemeler daha fazla boş alan isteyebilir.",
    },
    "tweak.storagesense.desc": {
        "en": "Turns off Storage Sense automatic cleanup.",
        "tr": "Depolama Algısı otomatik temizliğini kapatır.",
    },
    "tweak.ipv4pref.desc": {
        "en": "Makes Windows prefer IPv4 without fully disabling IPv6. Don’t combine with Disable IPv6 — pick one. Reboot recommended.",
        "tr": "IPv6’yı tamamen kapatmadan Windows’un IPv4’ü tercih etmesini sağlar. IPv6’yı kapat ile birlikte kullanma — birini seç. Yeniden başlatma önerilir.",
    },
    "tweak.teredo.desc": {
        "en": "Disables the Teredo IPv6 tunneling interface via netsh.",
        "tr": "netsh ile Teredo IPv6 tünel arayüzünü kapatır.",
    },
    "tweak.utctime.desc": {
        "en": "Treats the hardware clock as UTC (helpful for dual-boot with Linux). On Windows-only PCs the clock can skew until corrected. Reboot required.",
        "tr": "Donanım saatini UTC sayar (Linux ile çift boot için işe yarar). Sadece Windows’lu PC’lerde saat kayabilir. Yeniden başlatma gerekir.",
    },
    "tweak.svchostsplit.desc": {
        "en": "Groups services into fewer svchost processes. Dangerous — instability can take down more services at once. Reboot required.",
        "tr": "Servisleri daha az svchost sürecinde gruplar. Tehlikeli — kararsızlık daha fazla servisi birden düşürebilir. Yeniden başlatma gerekir.",
    },
    "tweak.traynotif.desc": {
        "en": "Disables Notification Center and toast notifications. You’ll miss system and app alerts.",
        "tr": "Bildirim Merkezi ve toast bildirimlerini kapatır. Sistem ve uygulama uyarılarını kaçırırsın.",
    },
    "tweak.longpaths.desc": {
        "en": "Allows paths longer than 260 characters for compatible apps.",
        "tr": "Uyumlu uygulamalar için 260 karakterden uzun yollara izin verir.",
    },
    "tweak.verboselogon.desc": {
        "en": "Shows detailed status text while signing in or out.",
        "tr": "Oturum açarken / kapatırken ayrıntılı durum metni gösterir.",
    },
    "tweak.bsodverbose.desc": {
        "en": "Shows technical BSOD details and disables the sad-face emoticon.",
        "tr": "Mavi ekranda teknik ayrıntıları gösterir ve üzgün yüz emojisini kapatır.",
    },
    "tweak.numlock.desc": {
        "en": "Turns Num Lock on for the logon screen and the current user.",
        "tr": "Oturum açma ekranı ve geçerli kullanıcı için Num Lock’u açık getirir.",
    },
    "tweak.scrollbars.desc": {
        "en": "Keeps scrollbars always visible instead of auto-hiding.",
        "tr": "Kaydırma çubuklarını otomatik gizleme yerine her zaman görünür tutar.",
    },
    "tweak.stickykeys.desc": {
        "en": "Disables Sticky Keys so the Shift-key popup doesn’t interrupt games.",
        "tr": "Yapışkan Tuşlar’ı kapatır; Shift uyarısı oyunu bölmez.",
    },
    "tweak.settingshome.desc": {
        "en": "Hides the Windows 11 Settings home / recommendation page.",
        "tr": "Windows 11 Ayarlar ana / öneri sayfasını gizler.",
    },
    "tweak.lockscreen.desc": {
        "en": "Skips the lock screen and goes straight to the password/PIN prompt. Slightly weaker if someone has physical access to the PC.",
        "tr": "Kilit ekranını atlayıp doğrudan parola/PIN ekranına gider. PC’ye fiziksel erişimi olan biri için biraz daha zayıf kalır.",
    },
    "tweak.s0standby.desc": {
        "en": "Allows network connectivity during Modern Standby (S0) while on AC power.",
        "tr": "Modern Bekleme (S0) sırasında, prize takılıyken ağ bağlantısına izin verir.",
    },
    "tweak.s3sleep.desc": {
        "en": "Prefers classic S3 sleep on supported hardware. Not all modern laptops support it; reboot required.",
        "tr": "Destekleyen donanımda klasik S3 uykuyu tercih eder. Her modern laptop desteklemez; yeniden başlatma gerekir.",
    },
    "tweak.batterypercent.desc": {
        "en": "Shows the numeric battery percentage in the system tray.",
        "tr": "Sistem tepsisinde sayısal batarya yüzdesini gösterir.",
    },
    "tweak.loginblur.desc": {
        "en": "Disables the acrylic blur behind the sign-in screen.",
        "tr": "Oturum açma ekranındaki akrilik bulanıklığı kapatır.",
    },
    "tweak.windowsnap.desc": {
        "en": "Turns off Aero Snap / window arrangement. Useful if snapping fights your games or multi-monitor layout.",
        "tr": "Aero Snap / pencere yerleştirme’yi kapatır. Yapıştırma oyununu veya çoklu monitör düzenini bozuyorsa işe yarar.",
    },
    "tweak.mpo.desc": {
        "en": "Disables DWM multiplane overlays. Try this for black screens, stutter, or overlay bugs with some GPUs/fullscreen games. Reboot required.",
        "tr": "DWM çok düzlemli kaplamaları kapatır. Bazı GPU / tam ekran oyunlarda siyah ekran, takılma veya overlay hatalarında dene. Yeniden başlatma gerekir.",
    },
    "tweak.feathyperv.desc": {
        "en": "Turns the Hyper-V Windows feature on or off. Needed for some VMs and sandboxes; uses resources when enabled.",
        "tr": "Hyper-V Windows özelliğini açar veya kapatır. Bazı sanal makineler ve sandbox için gerekir; açıkken kaynak kullanır.",
    },
    "tweak.featwsl.desc": {
        "en": "Turns Windows Subsystem for Linux on or off.",
        "tr": "Linux için Windows Alt Sistemi’ni (WSL) açar veya kapatır.",
    },
    "tweak.featsandbox.desc": {
        "en": "Turns Windows Sandbox on or off. Needs virtualization support.",
        "tr": "Windows Sandbox’ı açar veya kapatır. Sanallaştırma desteği ister.",
    },
    "tweak.featdotnet.desc": {
        "en": "Toggles older .NET Framework features some desktop apps still need.",
        "tr": "Bazı masaüstü uygulamaların hâlâ istediği eski .NET Framework özelliklerini açıp kapatır.",
    },
    "tweak.featmedia.desc": {
        "en": "Toggles legacy Windows media features (older codecs / Media Features).",
        "tr": "Eski Windows medya özelliklerini (eski codec / Media Features) açıp kapatır.",
    },
    "tweak.featnfs.desc": {
        "en": "Toggles the NFS client feature for mounting NFS network shares.",
        "tr": "NFS ağ paylaşımlarını bağlamak için NFS istemci özelliğini açıp kapatır.",
    },
    "tweak.featf8.desc": {
        "en": "Restores the legacy F8 advanced boot menu behavior where supported.",
        "tr": "Desteklenen yerlerde eski F8 gelişmiş önyükleme menüsü davranışını geri getirir.",
    },
    "tweak.wurecommended.desc": {
        "en": "Defers feature updates by 365 days and quality updates by 4 days, and keeps drivers out of Windows Update. Applies on Pro, Enterprise, Education, LTSC, and Windows Server. Home ignores these policies.",
        "tr": "Özellik güncellemelerini 365 gün, kalite güncellemelerini 4 gün erteler ve sürücüleri Windows Update dışında bırakır. Pro, Enterprise, Education, LTSC ve Windows Server’da geçerlidir. Home bu ilkeleri yok sayar.",
    },
    "preset.gamer.desc": {
        "en": "One click for Game Mode, Game Bar/DVR off, a strong power plan, MMCSS, lighter visuals, less telemetry, SysMain off, and similar gaming tweaks. Read the preview list, then apply on a gaming PC.",
        "tr": "Tek tıkta Game Mode, Game Bar/DVR kapalı, güçlü güç planı, MMCSS, sade görseller, daha az telemetri, SysMain kapalı ve benzeri oyun ayarları. Önizleme listesini oku, sonra oyuncu PC’sinde uygula.",
    },
    "preset.balanced.desc": {
        "en": "Light touch: Game Mode plus basic privacy, without aggressive service shutdowns. For when you want a bit of polish without risking Windows.",
        "tr": "Hafif dokunuş: Game Mode ve temel gizlilik; agresif servis kapatma yok. Windows’u riske atmadan biraz toparlamak isteyenler için.",
    },
    "preset.minimal.desc": {
        "en": "As lean as possible — telemetry, animations, background apps, unneeded services. Better for a second PC or HTPC; try Gamer first on your main gaming machine.",
        "tr": "Olabildiğince sade — telemetri, animasyon, arka plan uygulamaları, gereksiz servisler. İkinci PC veya HTPC için daha uygun; ana oyun PC’sinde önce Gamer’ı dene.",
    },
    "preset.laptop.desc": {
        "en": "Skips aggressive gaming power tweaks and focuses on telemetry cuts, lighter visuals, and fewer background apps — balanced for battery life.",
        "tr": "Agresif oyun güç ayarlarını atlar; telemetri kısma, sade görseller ve daha az arka plan uygulamasına odaklanır — batarya ömrü için dengeli.",
    },
    "preset.privacymax.desc": {
        "en": "More privacy than Balanced without going full Minimal: telemetry, location, advertising ID, and personalization tweaks in one profile.",
        "tr": "Balanced’tan daha fazla gizlilik, Minimal kadar sert değil: telemetri, konum, reklam kimliği ve kişiselleştirme ayarları tek profilde.",
    },
    "preset.defaults.desc": {
        "en": "Reverts reversible changes from this WinCleaner session (Undo All). Removed apps don’t come back — reinstall those from the Store.",
        "tr": "Bu WinCleaner oturumundaki geri alınabilir değişiklikleri geri alır (Tümünü Geri Al). Silinen uygulamalar dönmez — onları Store’dan kurarsın.",
    },
    "net.dns.desc": {
        "en": "Changes DNS servers on adapters that have IP enabled, then flushes the DNS cache. Needs Administrator.",
        "tr": "IP’si açık adaptörlerde DNS sunucularını değiştirir, sonra DNS önbelleğini temizler. Yönetici gerekir.",
    },
    "tools.takeOwn.desc": {
        "en": "Adds Take Ownership to file and folder right-click menus. Needs Administrator; turning it off removes the menu entries.",
        "tr": "Dosya ve klasör sağ tık menüsüne Sahipliği Al ekler. Yönetici ister; kapatınca menü girdileri silinir.",
    },
    "tools.ntp.desc": {
        "en": "Syncs the clock with an NTP time server so Windows time stays accurate.",
        "tr": "Saati bir NTP zaman sunucusuyla senkronlar; Windows saati doğru kalır.",
    },
    "tools.autologon.desc": {
        "en": "Signs in automatically with the selected account (stores credentials). Convenient on a home PC — weaker if others can touch the machine.",
        "tr": "Seçili hesapla otomatik oturum açar (kimlik bilgisi saklanır). Ev PC’sinde rahat; başkası makineye dokunabiliyorsa daha zayıf.",
    },
    "tools.wingetRepair.desc": {
        "en": "Repairs or re-registers winget / App Installer so package installs work again.",
        "tr": "Paket kurulumları yine çalışsın diye winget / App Installer’ı onarır veya yeniden kaydeder.",
    },
    "tm.dns.desc": {
        "en": "Sets DNS servers on IP-enabled adapters, then flushes the resolver cache.",
        "tr": "IP’si açık adaptörlerde DNS sunucularını ayarlar, sonra çözümleyici önbelleğini temizler.",
    },
    "tools.desc.restore": {
        "en": "Create a Windows restore point before you change things.",
        "tr": "Değişiklikten önce bir Windows geri yükleme noktası oluşturur.",
    },
    "tools.desc.cleanTemp": {
        "en": "Deletes Windows and user temporary files.",
        "tr": "Windows ve kullanıcı geçici dosyalarını siler.",
    },
    "tools.desc.diskCleanup": {
        "en": "Opens the built-in Disk Cleanup tool.",
        "tr": "Yerleşik Disk Temizleme aracını açar.",
    },
    "tools.desc.sfcDism": {
        "en": "Repairs system files — can take a long time.",
        "tr": "Sistem dosyalarını onarır; uzun sürebilir.",
    },
    "tools.desc.winsxs": {
        "en": "Cleans old Windows Update components — harder to roll back afterward.",
        "tr": "Eski Windows Update bileşenlerini temizler; sonra geri almak zorlaşır.",
    },
    "tools.desc.godMode": {
        "en": "Creates a desktop shortcut to every Control Panel item.",
        "tr": "Masaüstüne tüm Denetim Masası öğelerine giden bir kısayol oluşturur.",
    },
    "tools.desc.wuReset": {
        "en": "Resets Windows Update services and caches.",
        "tr": "Windows Update servislerini ve önbelleklerini sıfırlar.",
    },
    "tools.desc.networkReset": {
        "en": "Resets the Winsock/IP stack — reboot afterward is recommended.",
        "tr": "Winsock/IP yığınını sıfırlar; ardından yeniden başlatman önerilir.",
    },
    "tools.desc.memoryDiag": {
        "en": "Opens Windows Memory Diagnostic.",
        "tr": "Windows Bellek Tanılaması’nı açar.",
    },
    "tools.desc.diskAnalyze": {
        "en": "Shows free space across your drives.",
        "tr": "Sürücülerindeki boş alanı gösterir.",
    },
}
