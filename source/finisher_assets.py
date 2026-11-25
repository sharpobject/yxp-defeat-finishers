"""Validate every authored costume/color battle rig used by the finisher's direct indices.

The checked bytes are the shipped SkeletonDataAsset input, not a file exported
by this mod. A changed rig must be inspected and its choreography reauthored
before installation; the running game never searches for or validates bones.
"""
from __future__ import annotations
import base64
import hashlib
import json
from pathlib import Path
import struct
import warnings

# Inspected with the shipped spine-csharp SkeletonBinary reader (Spine 4.2.43).
# Animation order, bone order and setup/animation transforms are all bound by
# the complete binary hash, including the named operations in VisualsPayload.
RIGS = (
    ("Character_Battle_1000004_0", "林小月.skel",
     "1865adda740130fcb9bb1022a4453fd5bcb42456212593501c863f3909dec03f"),
    ("Character_Battle_1000004_0_1", "林小月-焕彩1.skel",
     "b85f4147f6a950149adb370b604dfa2da412978f83ddc18056faa9df3eae9300"),
    ("Character_Battle_1000004_1", "林小月落雪娘战斗.skel",
     "6e59e63ac66faa8bdb4c813c8f530bff797521087882ae5f547ba2f82f74e346"),
    ("Character_Battle_1000004_1_1", "林小月落雪娘战斗-焕彩1.skel",
     "62e0e91be5fe5cf4e59c244d3df998b93d0f7ad16890026855d5d63cde2d45c6"),
    ("Character_Battle_1000004_2", "林小月眠狸谣战斗.skel",
     "7738085f02e4780131b1a36d74b56f3f3c9ca3d1b9115b8e8aaa4020d6216661"),
    ("Character_Battle_4000002_0", "屠馗战斗动画.skel",
     "48648b25c55a8f3d44d5289dd8dc6a9416876aec7057cb5f87054c29ce5ef584"),
    ("Character_Battle_4000002_0_1", "屠馗战斗动画-焕彩1.skel",
     "42a045932ba3d90ced53d37118e70a421a4b2cb06168f5de78d95a5c3e374555"),
    ("Character_Battle_4000002_1", "屠馗热血化气战斗动画.skel",
     "15d1e6e2681076dfbea1650bdd21a18854cf5256eaadd93a0a1ca793a2287233"),
    ("Character_Battle_4000002_1_1", "屠馗热血化气战斗-焕彩1.skel",
     "a7f0e39560fc8615a2b9400d9625c6043a4119c59307065ba6b469d803a1c1ee"),
)


def _protobuf_fields(data: bytes):
    """Read the shipped xresloader container and CharacterSkinConfig wire fields."""
    position = 0
    def varint():
        nonlocal position
        value = shift = 0
        while True:
            byte = data[position]
            position += 1
            value |= (byte & 127) << shift
            if byte < 128:
                return value
            shift += 7
    while position < len(data):
        tag = varint()
        wire = tag & 7
        if wire == 0:
            value = varint()
        elif wire == 2:
            size = varint()
            value = data[position:position + size]
            position += size
        else:
            raise ValueError("CharacterSkinConfig protobuf wire layout changed")
        yield tag >> 3, value


def validate_finisher_rigs(catalog_path: Path, bundle_directory: Path) -> list[dict]:
    """Bind every configured costume/color prefab to its inspected Spine bytes."""
    import UnityPy
    UnityPy.config.FALLBACK_UNITY_VERSION = "2020.3.49f1"
    catalog = json.loads(Path(catalog_path).read_text())
    bucket_data, key_data, entries = (
        base64.b64decode(catalog[k])
        for k in ("m_BucketDataString", "m_KeyDataString", "m_EntryDataString"))
    buckets = []
    position = 4
    for _ in range(struct.unpack_from("<i", bucket_data)[0]):
        key_position, count = struct.unpack_from("<ii", bucket_data, position)
        position += 8
        ids = struct.unpack_from("<" + "i" * count, bucket_data, position)
        position += count * 4
        key = None
        if key_data[key_position] in (0, 1):
            length = struct.unpack_from("<i", key_data, key_position + 1)[0]
            key = key_data[key_position + 5:key_position + 5 + length].decode(
                "ascii" if key_data[key_position] == 0 else "utf-16le")
        buckets.append((key, ids))
    def entry(number):
        return struct.unpack_from("<7i", entries, 4 + number * 28)
    declared = {row[0] for row in RIGS}
    present = {name for name, ids in buckets if name and
               name.startswith(("Character_Battle_1000004_", "Character_Battle_4000002_")) and
               any(catalog["m_resourceTypes"][entry(number)[6]]["m_ClassName"] == "UnityEngine.GameObject"
                   for number in ids)}
    if present != declared:
        raise ValueError("Finisher costume inventory changed; reauthor before install: " +
                         repr(sorted(present ^ declared)))
    environments = {}
    def load(key, asset_type):
        matches = [number for name, ids in buckets if name == key for number in ids
                   if catalog["m_resourceTypes"][entry(number)[6]]["m_ClassName"] == asset_type]
        if len(matches) != 1:
            raise ValueError("Finisher requires exactly one authored asset: " + key)
        dependency = entry(matches[0])[2]
        if dependency < 0:
            raise ValueError("Finisher asset has no authored bundle: " + key)
        bundle_entry = entry(buckets[dependency][1][0])
        if not catalog["m_ProviderIds"][bundle_entry[1]].endswith("AssetBundleProvider"):
            raise ValueError("Finisher asset bundle provider changed: " + key)
        path = Path(bundle_directory) / Path(catalog["m_InternalIds"][bundle_entry[0]]).name
        if path not in environments:
            with warnings.catch_warnings():
                warnings.simplefilter("ignore")
                environments[path] = UnityPy.load(str(path))
        return path, environments[path]
    _, config_environment = load("CharacterSkinConfig", "UnityEngine.TextAsset")
    configs = [obj.read() for obj in config_environment.objects if obj.type.name == "TextAsset"
               and obj.read().m_Name == "CharacterSkinConfig"]
    if len(configs) != 1:
        raise ValueError("Finisher requires one shipped CharacterSkinConfig")
    configured = set()
    for number, value in _protobuf_fields(configs[0].m_Script.encode("utf-8", "surrogateescape")):
        if number != 2:
            continue
        row = dict(_protobuf_fields(value))
        char_id = row.get(1)
        if char_id not in (1000004, 4000002):
            continue
        key = "Character_Battle_" + str(char_id) + "_" + str(row.get(2, 0))
        color = row.get(5, 0)
        if color:
            key += "_" + str(color)
        if key in configured:
            raise ValueError("Duplicate finisher character costume in CharacterSkinConfig: " + key)
        configured.add(key)
    if configured != declared:
        raise ValueError("Finisher configured costumes changed; inspect before install: " +
                         repr(sorted(configured ^ declared)))
    result = []
    for key, text_asset_name, expected in RIGS:
        path, environment = load(key, "UnityEngine.GameObject")
        objects = {obj.path_id: obj for obj in environment.objects}
        def local(pointer):
            if pointer["m_FileID"] != 0 or pointer["m_PathID"] not in objects:
                raise ValueError("Finisher rig references an uninspected external object: " + key)
            return objects[pointer["m_PathID"]]
        def components(game_object):
            return [local(item["component"]) for item in game_object["m_Component"]]
        prefabs = [obj.read_typetree() for obj in environment.objects if obj.type.name == "GameObject"
                   and obj.read().m_Name == key]
        if len(prefabs) != 1:
            raise ValueError("Finisher prefab root changed: " + key)
        root_transform = [obj.read_typetree() for obj in components(prefabs[0]) if obj.type.name == "Transform"]
        if len(root_transform) != 1:
            raise ValueError("Finisher prefab transform changed: " + key)
        children = [local(pointer).read_typetree() for pointer in root_transform[0]["m_Children"]]
        skeleton_children = [child for child in children
                             if local(child["m_GameObject"]).read().m_Name == "SkeletonAnimation"]
        if len(skeleton_children) != 1:
            raise ValueError("Finisher character skeleton child changed: " + key)
        child = skeleton_children[0]
        if child["m_LocalScale"] != {"x": 0.5, "y": 0.5, "z": 1.0}:
            raise ValueError("Finisher character skeleton scale changed: " + key)
        component_data = [obj.read_typetree() for obj in components(local(child["m_GameObject"]).read_typetree())
                          if obj.type.name == "MonoBehaviour"]
        skeleton_components = [data for data in component_data if "skeletonDataAsset" in data]
        if len(skeleton_components) != 1:
            raise ValueError("Finisher character skeleton component changed: " + key)
        data = local(skeleton_components[0]["skeletonDataAsset"]).read_typetree()
        if abs(data["scale"] - 0.01) > 0.0000001 or data["skeletonDataModifiers"]:
            raise ValueError("Finisher authored skeleton reader changed: " + key)
        text_asset = local(data["skeletonJSON"])
        raw_asset = text_asset.read()
        raw = raw_asset.m_Script.encode("utf-8", "surrogateescape")
        actual = hashlib.sha256(raw).hexdigest()
        if raw_asset.m_Name != text_asset_name or actual != expected:
            raise ValueError("Finisher rig changed; inspect and reauthor its numbered clips/bones before install: " + key)
        result.append({"prefab": key, "bundle": str(path), "text_asset": text_asset_name,
                       "path_id": text_asset.path_id, "sha256": actual, "size": len(raw)})
    return result


def prepare_steamroller(source: Path, output: Path) -> None:
    """Select original arcade pixels from the retained, hash-bound sprite sheet."""
    from PIL import Image
    expected_source = "32eabc11909c1beff7988388dfbf1fce90a2a967efd5e41b13f18e438586729d"
    expected_output = "902e76c38038a082787d1307fe3aaa8a5a75e2703e1c94123c0d4e3d67dd669b"
    if hashlib.sha256(Path(source).read_bytes()).hexdigest() != expected_source:
        raise ValueError("Arcade roadroller source differs from its reviewed sprite sheet")
    with Image.open(source) as image:
        image = image.convert("RGB")
        background = image.getpixel((0, 0))
        selected = image.crop((220, 3370, 600, 3580)).convert("RGBA")
        # The same sheet supplies the hood with no actor over it. Its exact
        # outline distinguishes the original roller pixels from Dio above it.
        hood = image.crop((2942, 5537, 3110, 5631))
        for x in range(210, selected.width):
            hood_x = x - 193
            ys = [y for y in range(hood.height) if hood.getpixel((hood_x, y)) != background] if hood_x < hood.width else []
            top = 58 + ys[0] if ys else 85
            for y in range(top):
                selected.putpixel((x, y), background + (255,))
        for y in range(selected.height):
            for x in range(selected.width):
                color = selected.getpixel((x, y))
                if color[:3] == background:
                    selected.putpixel((x, y), background + (0,))
        Path(output).parent.mkdir(parents=True, exist_ok=True)
        selected.crop((29, 39, 360, 199)).save(output)
    if hashlib.sha256(Path(output).read_bytes()).hexdigest() != expected_output:
        raise ValueError("Prepared arcade roadroller pixels differ from the reviewed asset")


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--catalog", required=True, type=Path)
    parser.add_argument("--bundles", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = validate_finisher_rigs(args.catalog, args.bundles)
    text = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.write_text(text)
    else:
        print(text, end="")
