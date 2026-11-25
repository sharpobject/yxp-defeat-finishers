"""Lower the calculator's remaining CLR delegates/LINQ during preparation.

These are specific current-game consumers, not a runtime collection adapter.
The two cosmetic lookups are deleted with their unused effect consumers.
Actual strategy membership and Carefree's attack count use ordinary loops.
"""
from __future__ import annotations

import re


def once(text: str, old: str, new: str) -> str:
    if text.count(old) != 1:
        raise ValueError("Calculator collection boundary changed: " + old[:100])
    return text.replace(old, new)


def block(text: str, marker: str, replacement: str) -> str:
    if text.count(marker) != 1:
        raise ValueError("Calculator block boundary changed: " + marker)
    start = text.index(marker)
    opening = text.index("{", start + len(marker))
    depth = 1
    end = opening + 1
    # Every selected block contains no brace in a string/comment. Exact markers
    # and the compiler/checker bind this small preparation to current inputs.
    while depth:
        if text[end] == "{": depth += 1
        elif text[end] == "}": depth -= 1
        end += 1
    return text[:start] + replacement + text[end:]


def strategy_body(path: str) -> str:
    return """{
            var strategies = %s?.strategies;
            if (strategies == null) return false;
            for (int index = 0; index < strategies.Count; index++)
                if (strategies[index].selected == strategyId) return true;
            return false;
        }""" % path


def transform(filename: str, text: str) -> str:
    if filename == "BattleCharacter.cs":
        for name, field in (("HasFateStrategy", "fateStrategyData"), ("HasXianMoStrategy", "xianMoData")):
            signature = "public bool " + name + "(int strategyId)"
            text = block(text, signature, signature + "\n        " +
                         strategy_body("battleTempData.playerData.privateData." + field))
    elif filename == "XianMoStrategyFunctions.cs":
        signature = "public static bool HasXianMoStrategy(BattleCharacter src, int strategyId)"
        text = block(text, signature, signature + "\n        " +
                     strategy_body("src.battleTempData.playerData.privateData.xianMoData"))
    elif filename == "BattleExecuter.cs":
        text = block(text, "if (isJudge || context.isTowerMultiMode || context.isTA50Mode)", "")
    elif filename == "Card_339.cs":
        start = text.index("battleDeckIdList.ForEach(delegate (int card)")
        end = text.index("            });", start) + len("            });")
        text = text[:start] + """for (int cardIndex = 0; cardIndex < battleDeckIdList.Count; cardIndex++)
            {
                if (src.IsPuTongGongJi(battleDeckIdList[cardIndex])) attackCount++;
            }""" + text[end:]
    elif filename == "CardActionBase.cs":
        text = once(text, "        protected CardFXConfig m_CardFXConfig;\n", "")
        text = block(text, "private set\n", "private set { m_CardConfig = value; }")
        text = block(text, "if (!BattleCharacter.IsAnimationFXEmpty(m_CardFXConfig.animFX))", "")
        # Only integer literals and already-evaluated local variables occur in
        # these seven arrays, so direct comparisons preserve evaluation order.
        pattern = r"new int\[\d+\]\s*\{\s*([\d,\s]+)\}\s*\.Contains\((id|value|num)\)"
        def comparison(match: re.Match) -> str:
            values = re.findall(r"\d+", match[1])
            return "(" + " || ".join(match[2] + " == " + value for value in values) + ")"
        text, count = re.subn(pattern, comparison, text)
        if count != 7: raise ValueError("Adjacent literal membership count changed: " + str(count))
        for name, values in (("yunjiandianxing1", ["1000069", "1010069", "1020069"]),
                             ("yunjiandianxing2", ["1030069", "1040069"])):
            declaration = r"int\[\] " + name + r" = new int\[\d+\]\s*\{\s*([\d,\s]+)\};"
            match = re.search(declaration, text)
            if match is None or re.findall(r"\d+", match[1]) != values:
                raise ValueError("Adjacent named membership changed: " + name)
            text = text[:match.start()] + text[match.end():]
            text = once(text, name + ".Contains(num)",
                        "(" + " || ".join("num == " + value for value in values) + ")")
        pattern = r'new string\[5\]\s*\{\s*"金灵",\s*"水灵",\s*"木灵",\s*"火灵",\s*"土灵"\s*\}\.Any\(\(string element\) => cardConfig.name.Contains\(element\)\)'
        text, count = re.subn(pattern, "(" + " || ".join('cardConfig.name.Contains("' + name + '")'
                             for name in ("金灵", "水灵", "木灵", "火灵", "土灵")) + ")", text)
        if count != 1: raise ValueError("Five-element card-name membership changed.")
        text = once(text, "    using System.Linq;\n", "")
    elif filename in ("Card_17.cs", "Card_428.cs"):
        text = block(text, "if (m_CardFXConfig.skinNumber == 4005)", "")
    elif filename == "Card_5000015.cs":
        text = once(text, "            string permanent = m_CardFXConfig.animFX.permanent;\n", "")
        text = block(text, "if (!string.IsNullOrEmpty(permanent))", "")
    return text
