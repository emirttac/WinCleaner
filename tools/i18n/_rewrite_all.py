# -*- coding: utf-8 -*-
"""
Rewrite setting descriptions into natural prose for all languages.
EN/TR use handcrafted copy; other langs strip FAQ labels from SOURCE_CODES XAML.
"""
from __future__ import annotations

import html
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
I18N = Path(__file__).resolve().parent
XAML_DIR = ROOT / "SOURCE_CODES" / "src" / "WinCleaner" / "Resources"
LANGS = ["en", "tr", "de", "es", "zh", "fr", "ro", "ru"]

UNDO_LABEL_RE = re.compile(
    r"(?i)^(can you undo|geri alınır|rückgängig|se puede deshacer|¿se puede|"
    r"annulation possible|puteți anula|можно ли отменить|可否撤销|"
    r"pouvez-vous annuler)"
)
YES_RE = re.compile(
    r"(?is)^(yes|evet|ja|sí|si|oui|da|да|可以|是)[.。]?\s*(?:[-–—:：]\s*)?(.*)$"
)
NOOP_OFF_RE = re.compile(
    r"(?i)^(back to normal|zurück zum normal|eski haline|"
    r"vuelve( el)? comportamiento|vuelve el comportamiento|"
    r"retour au comportement|revient au comportement|"
    r"revenire la comportament|reveniți la comportament|"
    r"возврат к обычному|вернуться к обычному|"
    r"恢复正常|正常行为)"
)
# Labels that mean "when off / if remove" — we keep the body as-is (already often complete)
OFFISH = re.compile(
    r"(?i)(deaktiv|ausschalt|entfernen|off|remove|desactiv|opri|отключ|удал|"
    r"关闭|移除|désactiv|retrait|kapat|kaldır)"
)


def parse_xaml(path: Path) -> dict[str, str]:
    text = path.read_text(encoding="utf-8")
    out: dict[str, str] = {}
    for m in re.finditer(
        r'<sys:String\s+x:Key="([^"]+)">(.*?)</sys:String>', text, re.S
    ):
        raw = m.group(2).replace("&#x0a;", "\n").replace("&#10;", "\n")
        out[m.group(1)] = html.unescape(raw).strip()
    return out


def ensure_sentence(s: str, lang: str) -> str:
    s = re.sub(r"\s+", " ", s).strip()
    if not s:
        return s
    if lang != "zh" and s[0].islower():
        s = s[0].upper() + s[1:]
    if s[-1] not in ".!?…。！？":
        s += "." if lang != "zh" else "。"
    return s


def split_label(line: str) -> tuple[str | None, str]:
    """Return (label, body) if line looks like 'ShortLabel: body'."""
    # Prefer fullwidth colon for CJK
    for sep in ("：", ":"):
        if sep in line:
            left, right = line.split(sep, 1)
            left, right = left.strip(), right.strip()
            if right and 1 <= len(left) <= 48 and "\n" not in left:
                # Avoid splitting URLs / times
                if left.count(" ") <= 8:
                    return left, right
    return None, line


def naturalize(text: str, lang: str) -> str:
    text = text.replace("\\n", "\n").replace("\r\n", "\n").replace("\r", "\n")
    # Some XAML stored literal newlines already collapsed in JSON with spaces — also
    # accept single-line FAQ joined with ". Label:"
    # First expand mid-string labels that look like ". Funktion:" etc. if multi-label one-liner
    lines = [ln.strip() for ln in text.split("\n") if ln.strip()]
    if len(lines) == 1:
        # Try to re-split one-liners that still contain multiple labels separated by ". "
        # Keep as one line if no clear structure
        pass

    parts: list[str] = []
    for line in lines:
        label, body = split_label(line)
        if label is None:
            parts.append(line)
            continue

        if UNDO_LABEL_RE.search(label):
            m = YES_RE.match(body)
            if m:
                rest = (m.group(2) or "").strip()
                # Nested note after Yes
                nested_label, nested_body = split_label(rest) if rest else (None, rest)
                if nested_label and nested_body:
                    body = nested_body
                else:
                    body = rest
                if not body:
                    continue
            elif not body or YES_RE.match(body + " "):
                continue

        if NOOP_OFF_RE.search(body.strip()):
            continue
        body = body.lstrip("。. ")
        if not body.strip() or body.strip() in {".", "。", "…"}:
            continue

        parts.append(body)

    # Dedup + sentence-ize
    out: list[str] = []
    seen: set[str] = set()
    for p in parts:
        p = ensure_sentence(p, lang)
        key = re.sub(r"\s+", " ", p.lower().rstrip(".。"))
        if not key or key in seen:
            continue
        seen.add(key)
        out.append(p)

    result = " ".join(out)
    result = re.sub(r"\s{2,}", " ", result).strip()
    # Remove leftover undo crumbs
    result = re.sub(
        r"(?i)\b(can you undo it|geri alınır mı|rückgängig möglich|¿?se puede deshacer\??|"
        r"annulation possible|puteți anula|можно ли отменить|可否撤销)\s*:\s*"
        r"(yes|evet|ja|sí|oui|da|да|可以)\.?",
        "",
        result,
    )
    return re.sub(r"\s{2,}", " ", result).strip(" ,;")


def load_handcrafted() -> dict[str, dict[str, str]]:
    import importlib.util

    def load_mod(name: str, file: Path):
        spec = importlib.util.spec_from_file_location(name, file)
        mod = importlib.util.module_from_spec(spec)
        assert spec and spec.loader
        spec.loader.exec_module(mod)
        return mod

    natural = load_mod("wc_natural", I18N / "_rewrite_natural.py")
    tweaks = load_mod("wc_tweaks", I18N / "_natural_tweaks.py")
    merged = dict(natural.NATURAL)
    merged.update(tweaks.NATURAL_TWEAKS)
    return merged


def looks_faq(text: str) -> bool:
    if "\n" in text or "\\n" in text or "&#x0a;" in text:
        return True
    markers = (
        "What it does:",
        "What it is:",
        "Ne işe yarar:",
        "Nedir:",
        "Funktion:",
        "Was es ist:",
        "Qué hace:",
        "Qué es:",
        "Rôle :",
        "Qu’est-ce",
        "Ce face:",
        "Ce este:",
        "Что делает:",
        "Что это:",
        "作用",
        "是什么",
        "Rückgängig",
        "Annulation possible",
        "Can you undo",
        "Geri alınır",
        "Puteți anula",
        "Можно ли отменить",
        "¿Se puede",
    )
    return any(m in text for m in markers)


def main() -> None:
    hand = load_handcrafted()
    report = []

    for lang in LANGS:
        xaml_path = XAML_DIR / f"Strings.{lang}.xaml"
        originals = parse_xaml(xaml_path)
        path = I18N / f"{lang}.json"
        data = json.loads(path.read_text(encoding="utf-8"))
        changed = 0

        for key in list(data.keys()):
            is_desc = key.endswith(".desc") or ".desc." in key
            if not is_desc or key.startswith("inst."):
                continue

            if lang in ("en", "tr") and key in hand and lang in hand[key]:
                new_val = hand[key][lang]
            else:
                src = originals.get(key, data[key])
                # FR/others may still have English FAQ in JSON for newer keys
                if not looks_faq(src) and looks_faq(str(data[key])):
                    src = data[key]
                if not looks_faq(src):
                    continue
                # If source is English FAQ leftover in non-en file, still naturalize
                new_val = naturalize(src, lang if not src.startswith("What it") else "en")

            if new_val and new_val != data[key]:
                data[key] = new_val
                changed += 1

        path.write_text(
            json.dumps(data, ensure_ascii=False, indent=4) + "\n", encoding="utf-8"
        )
        report.append(f"{lang}: {changed}")
        print(report[-1])

    # Samples
    samples = {}
    for lang in LANGS:
        samples[lang] = json.loads((I18N / f"{lang}.json").read_text(encoding="utf-8"))
    for k in ("svc.diagtrack.desc", "tweak.gamemode.desc", "app.solitaire.desc"):
        print(f"\n## {k}")
        for lang in LANGS:
            print(f"  {lang}: {samples[lang].get(k)}")


if __name__ == "__main__":
    main()
