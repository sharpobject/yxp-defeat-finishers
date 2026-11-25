"""Lower the existing detached calculator to the loader's C# 7.3 source subset.

Calculations, native configuration reads and card selection are retained. This
changes only language syntax and presentation-string construction. Collection
callbacks are adapted separately by prepare_collections.py.
"""
from pathlib import Path
import hashlib
import re


def once(text, old, new):
    if text.count(old) != 1:
        raise ValueError("Calculator source boundary changed: " + old[:100])
    return text.replace(old, new)


def format_numbers(match):
    pattern, arguments = match.groups()
    values = [part.strip() for part in arguments.split(',')]
    pieces = re.split(r'(\{\d+\})', pattern)
    result = []
    used = []
    for part in pieces:
        index = re.fullmatch(r'\{(\d+)\}', part)
        if index:
            n = int(index.group(1)); used.append(n)
            result.append('(' + values[n] + ').ToString(System.Globalization.CultureInfo.InvariantCulture)')
        elif part:
            result.append('"' + part + '"')
    if used != list(range(len(values))):
        raise ValueError("Formatting would change argument evaluation order")
    return '(' + ' + '.join(result) + ')'


def lower(name, text):
    if name in ('Card_19.cs', 'Card_126.cs'):
        # Every parameterless ToString here converts an integer talent value
        # embedded in the detached copy's updated card description.
        text = text.replace('.ToString()', '.ToString(System.Globalization.CultureInfo.InvariantCulture)')
    if name == 'Card_9.cs':
        text = once(text, '"Attack_" + (nextParam + 1)',
                    '"Attack_" + (nextParam + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)')
    if name == 'Presentation.cs':
        lines = []
        for line in text.splitlines(keepends=True):
            if re.search(r'\bnew\(', line):
                match = re.match(r'\s*(?:(?:public|private|internal|protected|static|readonly|sealed)\s+)*([\w.<> ,]+?)\s+(?:\w+|operator)\b', line)
                if not match:
                    raise ValueError('Unrecognized target-typed construction: ' + line)
                kind = 'Vector2' if 'implicit operator Vector2' in line else match.group(1).strip()
                line = re.sub(r'\bnew\(', 'new ' + kind + '(', line)
            lines.append(line)
        text = ''.join(lines)
    if name == 'BattleExecuter.cs':
        text = once(text, 'static CardItem SkipCardAnim(', 'CardItem SkipCardAnim(')
    if name == 'BattleCharacter.cs':
        text = once(text, '''return wuxing switch
            {
                BuffType.JiHuoMuLing => BuffType.JiHuoTuLing,
                BuffType.JiHuoTuLing => BuffType.JiHuoShuiLing,
                BuffType.JiHuoShuiLing => BuffType.JiHuoHuoLing,
                BuffType.JiHuoHuoLing => BuffType.JiHuoJinLing,
                BuffType.JiHuoJinLing => BuffType.JiHuoMuLing,
                _ => wuxing,
            };''', '''switch (wuxing)
            {
                case BuffType.JiHuoMuLing: return BuffType.JiHuoTuLing;
                case BuffType.JiHuoTuLing: return BuffType.JiHuoShuiLing;
                case BuffType.JiHuoShuiLing: return BuffType.JiHuoHuoLing;
                case BuffType.JiHuoHuoLing: return BuffType.JiHuoJinLing;
                case BuffType.JiHuoJinLing: return BuffType.JiHuoMuLing;
                default: return wuxing;
            }''')
        text = text.replace('string.Format("{0}-{1}", "命元".Translate(), damage)',
                            '("命元".Translate() + "-" + damage.ToString(System.Globalization.CultureInfo.InvariantCulture))')
        text = re.sub(r'string.Format\("\{0\}-\{1\}\(\{2\}\)", "命元"\.Translate\(\), damage, (text|"\w+"\.Translate\(\))\)',
                      lambda m: '("命元".Translate() + "-" + damage.ToString(System.Globalization.CultureInfo.InvariantCulture) + "(" + ' + m[1] + ' + ")")', text)
        text = re.sub(r'string.Format\("\{0\}-0\\n\(\{1\}-\{2\}\)", "命元"\.Translate\(\), ("[^"\n]+"\.Translate\(\)|TranslateUtil.GetTalentTranslate\(105\)), damage\)',
                      lambda m: '("命元".Translate() + "-0\\n(" + ' + m[1] + ' + "-" + damage.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")")', text)
    text = re.sub(r'string.Format\("([^"\n]*)", new object\[\] \{ ([^{}\n]*) \}\)', format_numbers, text)
    if 'string.Format(' in text:
        raise ValueError('Unadapted boxed presentation format in ' + name)
    return text


def prepare(source: Path, output: Path) -> dict:
    from prepare_collections import transform
    output.mkdir(parents=True, exist_ok=True)
    receipt = {}
    for path in sorted(source.glob('*.cs')):
        raw = path.read_text()
        text = transform(path.name, lower(path.name, raw))
        target = output / path.name
        target.write_text(text)
        receipt[path.name] = dict(source_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                                 output_sha256=hashlib.sha256(target.read_bytes()).hexdigest())
    return receipt
