# -*- coding: utf-8 -*-
"""Restore FAQ-style descriptions from SOURCE_CODES XAML and rewrite as natural prose."""
from __future__ import annotations

import html
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
I18N = Path(__file__).resolve().parent
XAML_DIR = ROOT / "SOURCE_CODES" / "src" / "WinCleaner" / "Resources"
LANGS = ["en", "tr", "de", "es", "zh", "fr", "ro", "ru"]

# (what_labels, off_labels, when_labels, undo_labels, result_labels, caution_labels, why_labels, benefit_labels, note_labels, for_labels, how_back_labels)
RULES: dict[str, dict[str, list[str] | dict[str, str]]] = {
    "en": {
        "what": ["What it does:", "What it is:"],
        "off": [
            "If you turn it off / set to Manual:",
            "If you turn it off:",
            "If you remove / disable it:",
            "If you remove it:",
            "If you do not have VR:",
            "If you do not play it:",
            "If you do not use it:",
            "If you never use it:",
            "If you check weather on your phone:",
            "If you hate news pushed at startup:",
            "If you use Discord or Teams:",
            "If you use Google Maps or your phone:",
            "If you use VLC or similar:",
            "If you use Spotify:",
            "If you do not edit videos:",
            "If you just Google things:",
            "If you already looked once:",
            "If you use another viewer:",
            "If you use something else:",
            "If you do not keep contacts here:",
            "Whether you have Office or not:",
            "If you do not manage child accounts on this PC:",
            "If you never built a flow:",
            "If you are not a developer using it:",
            "If you do not use in-game voice captions:",
            "If you use classic Outlook or another mail client:",
            "Setting to Manual:",
            "When enabled:",
            "When on:",
            "When off:",
        ],
        "when": ["When to disable:", "When to use:"],
        "undo": ["Can you undo it:"],
        "result": ["Result:", "Together with the previous setting:", "Meaning:", "Goal:"],
        "caution": ["Caution:", "Important:", "Downside:", "Bad case:"],
        "why": ["Why:", "Good case:", "Benefit:"],
        "note": ["Note:", "Gamer note:"],
        "for": ["For:"],
        "how": ["How to get it back:", "Revert:"],
        "conn": {
            "off": "Turning it off: {0}",
            "when": "Best when {0}",
            "result": "{0}",
            "caution": "Watch out: {0}",
            "why": "{0}",
            "note": "{0}",
            "for": "Made for {0}",
            "how": "To restore it: {0}",
            "undo_keep": "{0}",
        },
    },
    "tr": {
        "what": ["Ne işe yarar:", "Nedir:", "Ne'dir:", "Ne yapar:", "Ne:"],
        "off": [
            "Kapatınca / Manuel yapınca:",
            "Kapatınca ne olur:",
            "Kapatınca:",
            "Kapatırsan:",
            "Kaldırınca / kapatınca:",
            "Kaldırınca:",
            "VR’n yoksa:",
            "VR'n yoksa:",
            "Oynamıyorsan:",
            "Kullanmıyorsan:",
            "Hiç kullanmıyorsan:",
            "Hava durumuna telefondan bakıyorsan:",
            "Açılışta haber istemiyorsan:",
            "Discord veya Teams kullanıyorsan:",
            "Google Maps veya telefon kullanıyorsan:",
            "VLC veya benzeri kullanıyorsan:",
            "Spotify kullanıyorsan:",
            "Video düzenlemiyorsan:",
            "Zaten Google’a bakıyorsan:",
            "Bir kez baktıysan:",
            "Başka görüntüleyici kullanıyorsan:",
            "Başka bir şey kullanıyorsan:",
            "Kişileri burada tutmuyorsan:",
            "Office’in olsun olmasın:",
            "Bu PC’de çocuk hesabı yönetmiyorsan:",
            "Hiç akış kurmadıysan:",
            "Geliştirici olarak kullanmıyorsan:",
            "Oyun içi sesli altyazı kullanmıyorsan:",
            "Klasik Outlook veya başka posta kullanıyorsan:",
            "Manuel yapmak:",
            "Açıkken:",
            "Açık olduğunda:",
            "Kapalıyken:",
        ],
        "when": ["Ne zaman kapat:", "Ne zaman kullan:"],
        "undo": ["Geri alınır mı:"],
        "result": ["Sonuç:", "Önceki ayarla birlikte:", "Anlamı:", "Amaç:"],
        "caution": ["Dikkat:", "Önemli:", "Dezavantaj:", "Kötü durum:"],
        "why": ["Neden:", "İyi durum:", "Fayda:"],
        "note": ["Not:", "Oyuncu notu:"],
        "for": ["Kime:", "Kimler için:"],
        "how": ["Nasıl geri gelir:", "Geri getirme:", "Geri alma:"],
        "conn": {
            "off": "Kapatırsan {0}",
            "when": "Şu durumda mantıklı: {0}",
            "result": "{0}",
            "caution": "Dikkat: {0}",
            "why": "{0}",
            "note": "{0}",
            "for": "{0} için uygun.",
            "how": "Geri getirmek için: {0}",
            "undo_keep": "{0}",
        },
    },
    "de": {
        "what": ["Was es tut:", "Was es ist:"],
        "off": [
            "Wenn Sie es ausschalten / auf Manuell setzen:",
            "Wenn Sie es ausschalten:",
            "Wenn Sie es entfernen / deaktivieren:",
            "Wenn Sie es entfernen:",
            "Auf Manuell setzen:",
            "Wann aktiviert:",
            "Wenn aktiviert:",
            "Wenn aus:",
        ],
        "when": ["Wann ausschalten:", "Wann verwenden:"],
        "undo": ["Können Sie es rückgängig machen:"],
        "result": ["Ergebnis:", "Zusammen mit der vorherigen Einstellung:", "Bedeutung:", "Ziel:"],
        "caution": ["Vorsicht:", "Wichtig:", "Nachteil:", "Schlechter Fall:"],
        "why": ["Warum:", "Guter Fall:", "Vorteil:"],
        "note": ["Hinweis:", "Gamer-Hinweis:"],
        "for": ["Für:"],
        "how": ["Wie Sie es zurückbekommen:", "Rückgängig:"],
        "conn": {
            "off": "Wenn Sie es ausschalten: {0}",
            "when": "Sinnvoll, wenn {0}",
            "result": "{0}",
            "caution": "Vorsicht: {0}",
            "why": "{0}",
            "note": "{0}",
            "for": "Für {0}",
            "how": "Wiederherstellen: {0}",
            "undo_keep": "{0}",
        },
    },
    "es": {
        "what": ["Qué hace:", "Qué es:"],
        "off": [
            "Si lo desactivas / lo pones en Manual:",
            "Si lo desactivas:",
            "Si lo eliminas / desactivas:",
            "Si lo eliminas:",
            "Poner en Manual:",
            "Cuando está activado:",
            "Cuando está desactivado:",
        ],
        "when": ["Cuándo desactivar:", "Cuándo usar:"],
        "undo": ["¿Se puede deshacer?:", "Se puede deshacer:"],
        "result": ["Resultado:", "Junto con el ajuste anterior:", "Significado:", "Objetivo:"],
        "caution": ["Precaución:", "Importante:", "Desventaja:", "Mal caso:"],
        "why": ["Por qué:", "Buen caso:", "Beneficio:"],
        "note": ["Nota:", "Nota para gamers:"],
        "for": ["Para:"],
        "how": ["Cómo recuperarlo:", "Revertir:"],
        "conn": {
            "off": "Si lo desactivas: {0}",
            "when": "Tiene sentido si {0}",
            "result": "{0}",
            "caution": "Ojo: {0}",
            "why": "{0}",
            "note": "{0}",
            "for": "Pensado para {0}",
            "how": "Para recuperarlo: {0}",
            "undo_keep": "{0}",
        },
    },
    "zh": {
        "what": ["作用：", "是什么："],
        "off": ["关闭后 / 设为手动：", "关闭后：", "移除 / 禁用后：", "移除后：", "设为手动：", "启用时："],
        "when": ["何时关闭：", "何时使用："],
        "undo": ["能否撤销："],
        "result": ["结果：", "与上一项一起：", "含义：", "目标："],
        "caution": ["注意：", "重要：", "坏处：", "缺点："],
        "why": ["原因：", "好处：", "优点："],
        "note": ["备注：", "玩家提示："],
        "for": ["适用："],
        "how": ["如何恢复：", "还原："],
        "conn": {
            "off": "关闭后：{0}",
            "when": "适合：{0}",
            "result": "{0}",
            "caution": "注意：{0}",
            "why": "{0}",
            "note": "{0}",
            "for": "适合{0}",
            "how": "恢复方式：{0}",
            "undo_keep": "{0}",
        },
    },
    "fr": {
        "what": ["Ce que ça fait :", "Ce que c’est :", "Ce que c'est :"],
        "off": [
            "Si vous l’éteignez / passez en Manuel :",
            "Si vous l'éteignez / passez en Manuel :",
            "Si vous l’éteignez :",
            "Si vous l'éteignez :",
            "Si vous le retirez / désactivez :",
            "Si vous le retirez :",
            "Passer en Manuel :",
            "Quand c’est activé :",
            "Quand c'est activé :",
        ],
        "when": ["Quand désactiver :", "Quand utiliser :"],
        "undo": ["Pouvez-vous annuler :"],
        "result": ["Résultat :", "Avec le réglage précédent :", "Signification :", "But :"],
        "caution": ["Attention :", "Important :", "Inconvénient :", "Mauvais cas :"],
        "why": ["Pourquoi :", "Bon cas :", "Avantage :"],
        "note": ["Note :", "Note joueur :"],
        "for": ["Pour :"],
        "how": ["Comment le récupérer :", "Annuler :"],
        "conn": {
            "off": "Si vous le désactivez : {0}",
            "when": "Utile si {0}",
            "result": "{0}",
            "caution": "Attention : {0}",
            "why": "{0}",
            "note": "{0}",
            "for": "Pour {0}",
            "how": "Pour le récupérer : {0}",
            "undo_keep": "{0}",
        },
    },
    "ro": {
        "what": ["Ce face:", "Ce este:"],
        "off": [
            "Dacă îl opriți / setați pe Manual:",
            "Dacă îl opriți:",
            "Dacă o eliminați / dezactivați:",
            "Dacă o eliminați:",
            "Setare pe Manual:",
            "Când e activ:",
        ],
        "when": ["Când să dezactivați:", "Când să folosiți:"],
        "undo": ["Puteți anula:"],
        "result": ["Rezultat:", "Împreună cu setarea anterioară:", "Semnificație:", "Scop:"],
        "caution": ["Atenție:", "Important:", "Dezavantaj:", "Caz rău:"],
        "why": ["De ce:", "Caz bun:", "Beneficiu:"],
        "note": ["Notă:", "Notă pentru gameri:"],
        "for": ["Pentru:"],
        "how": ["Cum o aduceți înapoi:", "Anulare:"],
        "conn": {
            "off": "Dacă îl opriți: {0}",
            "when": "Are sens dacă {0}",
            "result": "{0}",
            "caution": "Atenție: {0}",
            "why": "{0}",
            "note": "{0}",
            "for": "Pentru {0}",
            "how": "Pentru a-l readuce: {0}",
            "undo_keep": "{0}",
        },
    },
    "ru": {
        "what": ["Что делает:", "Что это:"],
        "off": [
            "Если отключить / поставить Вручную:",
            "Если отключить:",
            "Если удалить / отключить:",
            "Если удалить:",
            "Поставить Вручную:",
            "Когда включено:",
        ],
        "when": ["Когда отключать:", "Когда использовать:"],
        "undo": ["Можно отменить:"],
        "result": ["Результат:", "Вместе с предыдущей настройкой:", "Смысл:", "Цель:"],
        "caution": ["Осторожно:", "Важно:", "Минус:", "Плохой случай:"],
        "why": ["Почему:", "Хороший случай:", "Плюс:"],
        "note": ["Примечание:", "Заметка геймера:"],
        "for": ["Для:"],
        "how": ["Как вернуть:", "Откат:"],
        "conn": {
            "off": "Если отключить: {0}",
            "when": "Имеет смысл, если {0}",
            "result": "{0}",
            "caution": "Осторожно: {0}",
            "why": "{0}",
            "note": "{0}",
            "for": "Для {0}",
            "how": "Как вернуть: {0}",
            "undo_keep": "{0}",
        },
    },
}

TRIVIAL_UNDO = re.compile(
    r"(?i)^(yes|evet|ja|sí|si|是|oui|da|да)\.?\s*$"
)

# Handcrafted overrides for EN + TR — highest quality, natural tone.
OVERRIDES: dict[str, dict[str, str]] = {}


def load_overrides() -> None:
    """Populate OVERRIDES with natural EN/TR copy."""
    # Filled below via exec of companion data for readability
    pass


def parse_xaml_strings(path: Path) -> dict[str, str]:
    text = path.read_text(encoding="utf-8")
    out: dict[str, str] = {}
    for m in re.finditer(
        r'<sys:String\s+x:Key="([^"]+)">(.*?)</sys:String>',
        text,
        flags=re.DOTALL,
    ):
        key, raw = m.group(1), m.group(2)
        raw = raw.replace("&#x0a;", "\n").replace("&#10;", "\n")
        raw = html.unescape(raw)
        out[key] = raw.strip()
    return out


def match_prefix(line: str, prefixes: list[str]) -> tuple[str | None, str]:
    lower = line.lower()
    for p in sorted(prefixes, key=len, reverse=True):
        if lower.startswith(p.lower()):
            return p, line[len(p) :].strip()
    return None, line


def ensure_sentence(s: str, lang: str) -> str:
    s = s.strip()
    if not s:
        return s
    if lang != "zh" and s[0].islower():
        s = s[0].upper() + s[1:]
    if s[-1] not in ".!?…。！？":
        s += "."
    return s


def naturalize_structured(text: str, lang: str) -> str:
    rules = RULES[lang]
    conn: dict[str, str] = rules["conn"]  # type: ignore
    text = text.replace("\\n", "\n").replace("\r\n", "\n").replace("\r", "\n")
    lines = [ln.strip() for ln in text.split("\n") if ln.strip()]
    if not lines:
        return text.strip()

    buckets: list[tuple[str, str]] = []
    for line in lines:
        kind = "plain"
        body = line
        for label_kind in (
            "what",
            "off",
            "when",
            "undo",
            "result",
            "caution",
            "why",
            "note",
            "for",
            "how",
        ):
            pref, rest = match_prefix(line, rules[label_kind])  # type: ignore
            if pref is not None:
                kind = label_kind
                body = rest
                break
        buckets.append((kind, body))

    # If nothing looked structured, just clean lightly
    if all(k == "plain" for k, _ in buckets) and len(buckets) == 1:
        return ensure_sentence(buckets[0][1], lang)

    parts: list[str] = []
    for kind, body in buckets:
        body = body.strip()
        if not body:
            continue
        if kind == "undo":
            # Drop pure "Yes." Keep useful trailing advice.
            # Patterns: "Yes. Foo" / "Evet. Foo"
            m = re.match(
                r"(?is)^(yes|evet|ja|sí|si|是|oui|da|да)\.?\s*(.*)$",
                body,
            )
            if m:
                rest = m.group(2).strip()
                if not rest or TRIVIAL_UNDO.match(body):
                    continue
                # Strip nested note labels inside undo line
                for nk in rules["note"]:  # type: ignore
                    if rest.lower().startswith(nk.lower()):
                        rest = rest[len(nk) :].strip()
                        break
                body = rest
                parts.append(ensure_sentence(conn["undo_keep"].format(body), lang))
            else:
                if TRIVIAL_UNDO.match(body):
                    continue
                parts.append(ensure_sentence(body, lang))
            continue

        if kind == "what":
            parts.append(ensure_sentence(body, lang))
        elif kind == "off":
            # Avoid "Turning it off: Back to normal" fluff — skip if too short/generic
            low = body.lower()
            skip_offs = (
                "back to normal behavior",
                "eski haline döner",
                "zurück zum normalen verhalten",
                "vuelve al comportamiento normal",
                "revient au comportement normal",
                "reveniți la comportamentul normal",
                "возврат к обычному поведению",
            )
            if any(s in low for s in skip_offs):
                continue
            # If body already starts with conditional, don't double-wrap awkwardly
            formatted = conn["off"].format(body[0].lower() + body[1:] if body and lang != "zh" else body)
            # Fix double punctuation / awkward "Kapatırsan arka..." capitalization for TR
            if lang == "tr" and formatted.startswith("Kapatırsan "):
                rest = formatted[len("Kapatırsan ") :]
                formatted = "Kapatırsan " + rest
            parts.append(ensure_sentence(formatted, lang))
        elif kind == "when":
            b = body
            # Strip leading "Only if" duplication feel
            formatted = conn["when"].format(b[0].lower() + b[1:] if b and lang != "zh" else b)
            # Remove trailing "Can you undo..." glued on same line leftovers
            for u in rules["undo"]:  # type: ignore
                idx = formatted.lower().find(u.lower())
                if idx > 0:
                    formatted = formatted[:idx].strip(" .")
            parts.append(ensure_sentence(formatted, lang))
        elif kind in ("result", "why", "note", "caution", "for", "how"):
            template = conn[kind]
            b = body
            if "{0}" in template:
                # lowercase first char when embedding mid-sentence for latin langs
                if lang != "zh" and kind in ("off", "when", "for") and b and b[0].isupper():
                    embed = b[0].lower() + b[1:]
                else:
                    embed = b
                parts.append(ensure_sentence(template.format(embed), lang))
            else:
                parts.append(ensure_sentence(b, lang))
        else:
            parts.append(ensure_sentence(body, lang))

    # Dedup
    out: list[str] = []
    seen: set[str] = set()
    for p in parts:
        key = re.sub(r"\s+", " ", p.lower().rstrip("."))
        if key in seen:
            continue
        seen.add(key)
        out.append(p)

    result = " ".join(out)
    result = re.sub(r"\s{2,}", " ", result)
    result = re.sub(r"\s+([,.!?])", r"\1", result)
    # Clean leftover label crumbs
    crumbs = [
        r"(?i)\bcan you undo it:\s*yes\.?",
        r"(?i)\bgeri alınır mı:\s*evet\.?",
        r"(?i)\bkönnen sie es rückgängig machen:\s*ja\.?",
        r"(?i)\b¿?se puede deshacer\??:\s*sí\.?",
        r"能否撤销：\s*是。?",
        r"(?i)\bpouvez-vous annuler\s*:\s*oui\.?",
        r"(?i)\bputeți anula:\s*da\.?",
        r"(?i)\bможно отменить:\s*да\.?",
    ]
    for c in crumbs:
        result = re.sub(c, "", result)
    result = re.sub(r"\s{2,}", " ", result).strip()
    return result


# --- Handcrafted natural EN + TR (authoritative) ---
NATURAL: dict[str, dict[str, str]] = {
    "svc.diagtrack.desc": {
        "en": "Sends Windows usage data to Microsoft. Turning it off cuts background reporting — most people notice nothing. It won’t magically raise FPS, but you get better privacy and less background noise.",
        "tr": "Windows’un kullanım verilerini Microsoft’a gönderir. Kapatırsan arka planda daha az rapor gider; çoğu kişi bir fark görmez. FPS mucizesi değil ama gizlilik artar, arka plan işi azalır.",
    },
    "svc.dmwappush.desc": {
        "en": "Old-style push and device-management messaging, plus related telemetry. Most home users can turn it off and never notice.",
        "tr": "Eski tip push / cihaz yönetimi mesajları ve ilgili telemetri. Ev kullanıcısının çoğu kapatır ve hiçbir şey fark etmez.",
    },
    "svc.wer.desc": {
        "en": "Sends crash reports to Microsoft when a program fails. It doesn’t prevent crashes — Manual is usually enough if you don’t care about those reports.",
        "tr": "Program çökünce Microsoft’a hata raporu yollar. Çökmeleri engellemez; rapora ihtiyacın yoksa Manuel bırakman çoğu zaman yeter.",
    },
    "svc.xblauth.desc": {
        "en": "Handles sign-in for Xbox, Microsoft account games, and Game Pass. Only disable it if you never use Xbox or Game Pass — otherwise the Xbox app, some games, and friends list can break.",
        "tr": "Xbox, Microsoft hesabı oyunları ve Game Pass girişini yönetir. Hiç Xbox veya Game Pass kullanmıyorsan kapatabilirsin; yoksa Xbox uygulaması, bazı oyunlar ve arkadaş listesi bozulabilir.",
    },
    "svc.xblgame.desc": {
        "en": "Syncs Xbox cloud saves. Local saves still work if you turn it off, but cloud sync may stop — be careful if you use Game Pass.",
        "tr": "Xbox bulut kayıtlarını senkronlar. Kapatırsan yerel kayıtlar durur ama bulut senkronu kesilebilir; Game Pass kullanıyorsan dikkat.",
    },
    "svc.xboxnet.desc": {
        "en": "Keeps Xbox online features connected. Turning it off can affect multiplayer and Xbox social features.",
        "tr": "Xbox’un online özelliklerinin ağ bağlantısını sağlar. Kapatırsan multiplayer ve Xbox sosyal özellikler etkilenebilir.",
    },
    "svc.xboxgip.desc": {
        "en": "Manages Xbox controllers and accessories. Leave it alone if you use a wireless Xbox controller.",
        "tr": "Xbox kolu ve aksesuarlarını yönetir. Kablosuz Xbox kol kullanıyorsan dokunma.",
    },
    "svc.spooler.desc": {
        "en": "Sends print jobs to your printer. If you never print at home, turning it off frees a useless idle service — but printing and PDF print will stop working.",
        "tr": "Yazıcıya iş gönderir. Evde hiç yazdırmıyorsan kapatmak boşuna ayakta duran bir servisi keser; yazıcı ve PDF yazdırma da çalışmaz.",
    },
    "svc.bthserv.desc": {
        "en": "Powers Bluetooth headphones, mice, and controllers. Only turn it off if you never use Bluetooth — otherwise Bluetooth dies completely.",
        "tr": "Bluetooth kulaklık, mouse ve kolları çalıştırır. Hiç Bluetooth kullanmıyorsan kapatabilirsin; kullanıyorsan tamamen ölür.",
    },
    "svc.onesync.desc": {
        "en": "Syncs Mail, Calendar, Contacts, and similar accounts. Built-in Mail/Calendar sync may break if you disable it.",
        "tr": "Mail, takvim ve kişiler gibi hesapları senkronlar. Kapatırsan yerleşik Mail/Takvim senkronu bozulabilir.",
    },
    "svc.cdp.desc": {
        "en": "Runs Nearby sharing and phone–PC bridge features. Games are fine without it; those sharing features just stop.",
        "tr": "Yakındaki paylaşım ve telefon–PC köprüsü gibi özellikleri çalıştırır. Oyunlara dokunmaz; sadece bu paylaşım özellikleri durur.",
    },
    "svc.diagsvc.desc": {
        "en": "Runs Windows troubleshooters in the background. Manual means it only runs when needed, with less daily load.",
        "tr": "Windows sorun gidericilerini arka planda çalıştırır. Manuel yaparsan sadece gerektiğinde çalışır, günlük yük azalır.",
    },
    "svc.dusm.desc": {
        "en": "Feeds the data-usage counter in Settings. Turning it off may break the counter, not your internet.",
        "tr": "Ayarlar’daki veri kullanımı sayacını besler. Kapatırsan sayaç bozulabilir; internetin kesilmez.",
    },
    "svc.sysmain.desc": {
        "en": "Tries to predict and preload apps into memory. On SSDs that can mean busy disk for little gain — many gamers turn it off to calm 100% disk spikes.",
        "tr": "Programları önceden tahmin edip belleğe yüklemeye çalışır. SSD’lerde bazen diski boşuna yorar; birçok oyuncu disk %100 olayını azaltmak için kapatır.",
    },
    "svc.wsearch.desc": {
        "en": "Builds a search index in the background and keeps poking the disk. Start search gets slower without it, but search still works and the disk rests.",
        "tr": "Arama için dosya indeksi tutar ve arka planda diski dürter. Kapatırsan Başlat araması yavaşlar ama yine çalışır; disk rahatlar.",
    },
    "svc.rpcss.desc": {
        "en": "Windows’s internal communication layer — almost everything depends on it. Disabling it breaks the system. WinCleaner locks this on purpose.",
        "tr": "Windows’un iç iletişim katmanı; neredeyse her şey buna bağlı. Kapatmak sistemi bozar. WinCleaner bunu kasten kilitledi.",
    },
    "svc.dcom.desc": {
        "en": "Lets programs call each other at a low level. Turning it off wrecks Windows. Locked on purpose.",
        "tr": "Programların birbirini düşük seviyede çağırmasını sağlar. Kapatmak Windows’u mahveder. Kasten kilitli.",
    },
    "svc.windefend.desc": {
        "en": "Windows’s built-in antivirus. Don’t disable it here even if you install another antivirus — WinCleaner blocks that path on purpose.",
        "tr": "Windows’un yerleşik antivirüsü. Başka antivirüs kursan bile buradan kapatma; WinCleaner bunu bilerek engelledi.",
    },
    "svc.wuauserv.desc": {
        "en": "Downloads and installs security updates. Stopping it means quiet days now and growing security holes later — only if you know what you’re doing.",
        "tr": "Güvenlik güncellemelerini indirir ve kurar. Kapatırsan bir süre rahat edersin ama güvenlik açığı birikir; sadece ne yaptığını biliyorsan.",
    },
    "svc.bits.desc": {
        "en": "Background downloads for Windows Update and some other apps. Turning it off can break updates and those downloads.",
        "tr": "Windows Update ve bazı uygulamaların arka plan indirmelerini yapar. Kapatırsan güncelleme ve o indirmeler bozulabilir.",
    },
    "svc.pcasvc.desc": {
        "en": "Watches for program compatibility issues and reports them to Microsoft. Apps still run if you turn it off; the prompts may just stop.",
        "tr": "Program uyumluluk sorunlarını izler ve Microsoft’a bildirir. Kapatırsan uygulamalar çalışmaya devam eder; uyarılar gelmeyebilir.",
    },
    "svc.lfsvc.desc": {
        "en": "Gives apps your location (pairs with the Deny Location tweak). Maps, weather, and other location features stop without it.",
        "tr": "Uygulamalara konum verir (Konumu reddet ayarıyla birlikte). Harita, hava durumu ve diğer konum özellikleri durur.",
    },
    "svc.remoteregistry.desc": {
        "en": "Lets other PCs on the network edit this machine’s registry remotely. Home users don’t need it — safer left off.",
        "tr": "Ağdaki başka bilgisayarların bu PC’nin registry’sine uzaktan erişmesine izin verir. Evde gerekmez; kapalı tutmak daha güvenli.",
    },
    "svc.ssdpsrv.desc": {
        "en": "Automatically finds UPnP devices on your network (smart TVs, printers, and so on). Internet still works if you turn it off; discovery may need to be manual.",
        "tr": "Ağdaki UPnP cihazlarını (akıllı TV, yazıcı vb.) otomatik bulur. Kapatırsan internet çalışır; cihaz keşfi elle gerekebilir.",
    },
    "svc.upnphost.desc": {
        "en": "Hosts UPnP devices together with SSDP Discovery. Some network device sharing may stop if you disable it.",
        "tr": "SSDP ile birlikte UPnP cihazlarını barındırır. Kapatırsan bazı ağ cihazı paylaşımları durabilir.",
    },
    "svc.retaildemo.desc": {
        "en": "Store / demo-mode service for retail PCs. Useless at home — safe to disable.",
        "tr": "Mağaza / demo modu servisi. Ev PC’sinde işe yaramaz; güvenle kapatılabilir.",
    },
    "svc.wmpnetwork.desc": {
        "en": "Shares Windows Media Player libraries over the home network (DLNA). Local playback is fine without it.",
        "tr": "Windows Media Player kitaplığını ev ağında paylaşır (DLNA). Yerel oynatma etkilenmez.",
    },
    "svc.tabletinput.desc": {
        "en": "Touch keyboard, pen, and handwriting. On a desktop or laptop without a touchscreen you can usually turn it off safely.",
        "tr": "Dokunmatik klavye, kalem ve el yazısı. Dokunmatik ekranı olmayan masaüstü/laptop’ta genelde güvenle kapatılır.",
    },
}


def finish_natural_dicts() -> None:
    """Extend NATURAL with apps, tweaks, presets — kept in this function for file size control."""
    apps = {
        "app.3dviewer.desc": (
            "Views 3D model files. Most people never need it — safe to remove, and you can get it back from the Microsoft Store.",
            "3D model dosyalarına bakmak için. Çoğu insanın ihtiyacı yok; güvenle kaldırılır, Store’dan geri gelir.",
        ),
        "app.mixedreality.desc": (
            "Setup portal for VR / mixed reality headsets. Without a headset it’s just wasted space — safe to remove.",
            "VR / karma gerçeklik kulaklığı kurulum kapısı. Kulaklığın yoksa sadece yer kaplar; güvenle kaldırılır.",
        ),
        "app.xboxapp.desc": (
            "The older Xbox companion app. Think twice if you use Game Pass — some social and Game Pass features may depend on it.",
            "Eski Xbox companion uygulaması. Game Pass kullanıyorsan iki kez düşün; bazı sosyal ve Game Pass özellikleri buna bağlı olabilir.",
        ),
        "app.xboxoverlay.desc": (
            "The Win+G Game Bar overlay — recording, widgets, FPS counter. Removing it cuts background capture load and can free a bit of FPS; reinstall from the Store if you miss it.",
            "Win+G Game Bar katmanı: kayıt, widget, FPS sayacı. Kaldırınca arka plan kayıt yükü azalır, biraz FPS kazandırabilir; özlersen Store’dan kurarsın.",
        ),
        "app.xboxidentity.desc": (
            "Identity provider for your Xbox account. Don’t remove it if you sign into Xbox services.",
            "Xbox hesabının kimlik sağlayıcısı. Xbox’a giriş yapıyorsan kaldırma.",
        ),
        "app.skype.desc": (
            "Classic Skype video chat. If you already use Discord or Teams, you can usually remove it.",
            "Klasik Skype görüntülü sohbet. Discord veya Teams kullanıyorsan genelde kaldırabilirsin.",
        ),
        "app.solitaire.desc": (
            "Microsoft’s card and casual games pack. If you don’t play it, it’s just sitting on disk — safe to remove.",
            "Microsoft’un iskambil / eğlence oyun paketi. Oynamıyorsan sadece yer kaplar; güvenle kaldırılır.",
        ),
        "app.weather.desc": (
            "Bing Weather app/widget. If you check weather on your phone, you don’t need it on the PC.",
            "Bing Hava Durumu uygulaması/widget’ı. Havaya telefondan bakıyorsan PC’de gerekmez.",
        ),
        "app.news.desc": (
            "Bing News / headlines app. Remove it if you don’t want news shoved at you on the PC.",
            "Bing Haberler uygulaması. PC’de haber dayatması istemiyorsan kaldır.",
        ),
        "app.officehub.desc": (
            "A “try / buy Office” promo hub. You don’t need it whether you own Office or not.",
            "“Office’i dene / satın al” tanıtım uygulaması. Office’in olsun olmasın buna ihtiyacın yok.",
        ),
        "app.people.desc": (
            "Contacts / address book app. Fine to remove if you don’t keep contacts there.",
            "Rehber / kişiler uygulaması. Kişileri burada tutmuyorsan kaldırabilirsin.",
        ),
        "app.feedback.desc": (
            "Sends feedback and suggestions to Microsoft. Safe to remove if you never open it.",
            "Microsoft’a geri bildirim / öneri yollar. Hiç açmıyorsan güvenle kaldırılır.",
        ),
        "app.gethelp.desc": (
            "Microsoft’s help chat and tips app. Skip it if you just search the web when something breaks.",
            "Microsoft’un yardım sohbeti ve ipucu uygulaması. Sorun olunca web’de arıyorsan gerekmez.",
        ),
        "app.getstarted.desc": (
            "New-PC tips tour. After the first look you’ll never need it again — safe to remove.",
            "Yeni PC ipuçları turu. Bir kez baktıktan sonra bir daha lazım olmaz; güvenle kaldırılır.",
        ),
        "app.maps.desc": (
            "Windows Maps. If you use Google Maps or your phone, you can drop this.",
            "Windows Haritalar. Google Maps veya telefon kullanıyorsan kaldırabilirsin.",
        ),
        "app.zunevideo.desc": (
            "Microsoft’s Films & TV player / store front. Fine to remove if you already use VLC or similar.",
            "Microsoft’un Filmler ve TV oynatıcısı / mağaza yüzü. VLC benzeri bir şey kullanıyorsan kaldırabilirsin.",
        ),
        "app.zunemusic.desc": (
            "The old Groove / music app. Unnecessary if you use Spotify or another player.",
            "Eski Groove / müzik uygulaması. Spotify veya başka oynatıcı kullanıyorsan gereksiz.",
        ),
        "app.clipchamp.desc": (
            "Simple video editor bundled with Windows 11. Remove it if you don’t edit video and want the space back.",
            "Windows 11 ile gelen basit video editörü. Video düzenlemiyorsan kaldırıp yer açabilirsin.",
        ),
        "app.teams.desc": (
            "Work/school meeting app that sometimes ships with Windows. Remove it if you don’t use it — leave it if work depends on it.",
            "Bazen Windows’la gelen iş/okul toplantı uygulaması. Kullanmıyorsan kaldır; işin buna bağlıysa bırak.",
        ),
        "app.store.desc": (
            "The Microsoft Store — Xbox, many apps, and updates come through here. Removing it puts the system in a bad spot, so the checkbox is locked.",
            "Microsoft Store — Xbox, birçok uygulama ve güncelleme buradan gelir. Kaldırmak sistemi zor durumda bırakır; bu yüzden kutu kilitli.",
        ),
        "app.photos.desc": (
            "Default photo and video viewer. Optional if you already use another viewer.",
            "Varsayılan fotoğraf ve video görüntüleyici. Başka bir uygulama kullanıyorsan isteğe bağlı kaldırılır.",
        ),
        "app.calculator.desc": (
            "The Calculator app. Marked critical in the catalog so you can’t delete it by accident here.",
            "Hesap makinesi. Katalogda kritik işaretli; burada yanlışlıkla silemezsin.",
        ),
        "app.yourphone.desc": (
            "Phone Link between your phone and PC. Safe to remove if you don’t use it; reinstall from the Store anytime.",
            "Telefon ile PC’yi bağlayan Phone Link. Kullanmıyorsan güvenle kaldırılır; Store’dan geri gelir.",
        ),
        "app.cortana.desc": (
            "Voice assistant app — limited on modern Windows. Safe to remove.",
            "Sesli asistan uygulaması; modern Windows’ta işlevi kısıtlı. Güvenle kaldırılır.",
        ),
        "app.widgets.desc": (
            "Powers the taskbar Widgets panel. Removing it also removes the Widgets icon — some Win11 setups change taskbar behavior afterward.",
            "Görev çubuğu Widget panelini çalıştırır. Kaldırınca Widget simgesi de gider; bazı Win11 kurulumlarında görev çubuğu davranışı değişebilir.",
        ),
        "app.paint3d.desc": (
            "3D drawing app. Most people stick with classic Paint — safe to remove.",
            "3D çizim uygulaması. Çoğu kişi klasik Paint’i kullanır; güvenle kaldırılır.",
        ),
        "app.print3d.desc": (
            "3D printing app. Useless without a 3D printer — safe to remove.",
            "3D yazdırma uygulaması. 3D yazıcın yoksa gereksiz; güvenle kaldırılır.",
        ),
        "app.todo.desc": (
            "Microsoft To Do task list. Safe to remove if you use something else.",
            "Microsoft To Do görev listesi. Başka bir uygulama kullanıyorsan güvenle kaldırılır.",
        ),
        "app.quickassist.desc": (
            "Remote support app. Handy in rare support cases — remove only if you’re sure you won’t need it.",
            "Uzaktan destek uygulaması. Nadir destek anlarında işe yarar; emin değilsen bırak.",
        ),
        "app.copilot.desc": (
            "Windows Copilot chat on the taskbar / Start. Remove carefully if you actually use it; you can reinstall from the Store.",
            "Görev çubuğu / Başlat’taki Windows Copilot sohbeti. Gerçekten kullanıyorsan dikkatli kaldır; Store’dan geri gelir.",
        ),
        "app.bingsearch.desc": (
            "Bing extras that inject web results into Windows Search. Local search still works without it.",
            "Windows Arama’ya web sonuçları sokan Bing paketi. Yerel arama yine çalışır.",
        ),
        "app.outlook.desc": (
            "The new Outlook for Windows app. Safe to remove if you use classic Outlook or another mail client — keep it if this is your daily mail.",
            "Yeni Outlook for Windows. Klasik Outlook veya başka posta kullanıyorsan kaldırabilirsin; günlük postan buysa bırak.",
        ),
        "app.stickynotes.desc": (
            "Sticky Notes. Safe to remove if you don’t use it; notes can come back via Store / Microsoft account.",
            "Yapışkan Notlar. Kullanmıyorsan güvenle kaldırılır; Store / Microsoft hesabından geri gelebilir.",
        ),
        "app.powerautomate.desc": (
            "Desktop automation / flows app. Safe to remove if you never built a flow.",
            "Masaüstü otomasyon / akış uygulaması. Hiç akış kurmadıysan güvenle kaldırılır.",
        ),
        "app.devhome.desc": (
            "Microsoft’s developer dashboard. Safe to remove if you’re not using it (Windows 11).",
            "Microsoft’un geliştirici paneli. Kullanmıyorsan güvenle kaldırılır (Windows 11).",
        ),
        "app.xboxtcui.desc": (
            "Xbox title-callable UI used by Game Bar / Game Pass popups. Leave it if you keep Game Bar or Game Pass.",
            "Game Bar / Game Pass pencerelerinde kullanılan Xbox arayüzü. Game Bar veya Game Pass tutuyorsan bırak.",
        ),
        "app.xboxspeech.desc": (
            "Xbox Game Bar speech-to-text overlay. Safe to remove if you don’t use in-game voice captions.",
            "Xbox Game Bar konuşmayı yazıya çevirme katmanı. Oyun içi sesli altyazı kullanmıyorsan kaldırabilirsin.",
        ),
        "app.family.desc": (
            "Family Safety / parental controls companion. Safe to remove if you don’t manage child accounts on this PC.",
            "Aile Güvenliği / ebeveyn kontrolü uygulaması. Bu PC’de çocuk hesabı yönetmiyorsan güvenle kaldırılır.",
        ),
        "app.startexperiences.desc": (
            "Start menu recommendations and suggested-app experiences on Windows 11. Start still works without it; suggestions just change.",
            "Windows 11 Başlat önerileri ve önerilen uygulama deneyimleri. Başlat çalışmaya devam eder; öneriler değişir.",
        ),
        "app.crossdevice.desc": (
            "Cross-device experience package between your Microsoft devices. Safe to remove if you don’t use those features.",
            "Microsoft cihazların arasında çapraz cihaz deneyimi paketi. Bu özellikleri kullanmıyorsan kaldırabilirsin.",
        ),
        "app.soundrecorder.desc": (
            "Simple voice recorder. Safe to remove if you don’t need it.",
            "Basit ses kaydedici. İhtiyacın yoksa güvenle kaldırılır.",
        ),
        "app.clock.desc": (
            "Clock, alarms, and timers app. Optional if you don’t use it.",
            "Saat, alarm ve zamanlayıcı uygulaması. Kullanmıyorsan isteğe bağlı kaldırılır.",
        ),
        "app.paint.desc": (
            "Classic Paint. Leave it unless you have a replacement — many people still open it for quick edits.",
            "Klasik Paint. Yerine bir şeyin yoksa bırak; çoğu kişi hızlı düzenleme için hâlâ açar.",
        ),
        "app.notepad.desc": (
            "Notepad. Locked as a basic system text editor so you don’t remove it by mistake.",
            "Not Defteri. Temel sistem metin düzenleyicisi olarak kilitli; yanlışlıkla kaldıramazsın.",
        ),
        "app.snip.desc": (
            "Snipping Tool / screenshot app. Keep it unless you already use ShareX or similar.",
            "Ekran Alıntısı / ekran görüntüsü aracı. ShareX benzeri bir şey kullanmıyorsan bırak.",
        ),
        "app.camera.desc": (
            "Camera app. Safe to remove on a desktop without a webcam.",
            "Kamera uygulaması. Webcam’i olmayan masaüstünde güvenle kaldırılır.",
        ),
    }
    for k, (en, tr) in apps.items():
        NATURAL[k] = {"en": en, "tr": tr}


finish_natural_dicts()


def main() -> None:
    # Load remaining EN/TR from companion module if present; otherwise continue with RULES rewrite
    companion = I18N / "_natural_tweaks.py"
    if companion.exists():
        ns: dict = {}
        exec(companion.read_text(encoding="utf-8"), ns)
        NATURAL.update(ns.get("NATURAL_TWEAKS", {}))

    for lang in LANGS:
        xaml = XAML_DIR / f"Strings.{lang}.xaml"
        originals = parse_xaml_strings(xaml)
        path = I18N / f"{lang}.json"
        data = json.loads(path.read_text(encoding="utf-8"))
        changed = 0
        for key, val in list(data.items()):
            if not key.endswith(".desc"):
                continue
            if key.startswith("inst."):
                continue
            # Prefer handcrafted for en/tr
            if lang in ("en", "tr") and key in NATURAL and lang in NATURAL[key]:
                new_val = NATURAL[key][lang]
            else:
                src = originals.get(key, val)
                # Only rewrite if it looks FAQ-ish
                if not any(
                    m in src
                    for m in (
                        "What it does:",
                        "What it is:",
                        "Ne işe yarar:",
                        "Nedir:",
                        "Ne yapar:",
                        "Was es tut:",
                        "Was es ist:",
                        "Qué hace:",
                        "Qué es:",
                        "作用：",
                        "是什么：",
                        "Ce que",
                        "Ce face:",
                        "Ce este:",
                        "Что делает:",
                        "Что это:",
                        "&#x0a;",
                        "\n",
                    )
                ):
                    continue
                new_val = naturalize_structured(src, lang)
            if new_val and new_val != data[key]:
                data[key] = new_val
                changed += 1
        path.write_text(json.dumps(data, ensure_ascii=False, indent=4) + "\n", encoding="utf-8")
        print(f"{lang}: updated {changed}")

    en = json.loads((I18N / "en.json").read_text(encoding="utf-8"))
    tr = json.loads((I18N / "tr.json").read_text(encoding="utf-8"))
    for k in ["svc.diagtrack.desc", "tweak.gamemode.desc", "app.xboxoverlay.desc", "svc.spooler.desc"]:
        print(f"\n{k}")
        print(" EN:", en.get(k))
        print(" TR:", tr.get(k))


if __name__ == "__main__":
    main()
