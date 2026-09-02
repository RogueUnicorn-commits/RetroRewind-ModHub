import argparse, base64, json, math, shutil, struct, sys, tempfile
from pathlib import Path

# Explicit allow-list for objects that may be transferred.
# Anything not listed here is intentionally excluded. This prevents newly
# introduced game objects, save metadata objects, standees, movie tapes,
# storage boxes, etc. from being transferred accidentally.
INCLUDED_CLASSES = {
    # Decorations
    'PosterFrame_C',
    'PosterFrame_Small_C',
    'PosterFrame_Standing_C',
    'TextSign_C',
    'Deco_DoorMat_BASE_C',
    'Deco_SCIFI_UFO_C',
    'Deco_Halloween_Jack_C',
    'Deco_Horror_Coffin_A_C',
    'Shelf_Movie-Display_4Row_01_C',
    'Shelf_Movie-Display_5Row_01_C',
    'Shelf_Movie-Display_6Row_01_C',
    'Shelf_Movie-Display_WallMounted_01_C',
    'SignNeon_Big_C',
    'SignNeon_Small_C',
    'Deco_Balloon_Color_C',
    'Deco_Balloon_Heart_C',
    'Deco_Balloon_Smiley_C',
    'CeillingSign_C',
    'Couch_C',
    'Deco_xMas_Gifts_C',
    'Deco_xMas_NutCracker_C',
    'Shelf_Movie-Shelf_MovieDisplay_Cabinet_01_C',
    'Shelf_Movie-Shelf_MovieDisplay_Unit_01_C',
    'Deco_Medieval_Armor_A_01_C',
    'Deco_Medieval_Flag_A_01_C',
    'Deco_Medieval_SwordShield_C',
    'Deco_CeilingFan_Colors_C',
    'Deco_CeilingFan_Neon_C',
    'Deco_CeilingFan_Wood_C',
    'Deco_Camera_C',
    'Deco_FilmWheel_C',
    'Deco_TheatreMask_C',
    'Deco_Light-Cinema_C',
    'Deco_Ceilling-Light_Color_A_01_C',
    'Deco_Robot_BASE_C',
    'Deco_LightBall_Ball_A_01_C',
    'Deco_LightBall_Ball_A_02_C',
    'Deco_Western_Cactus_A_C',
    'Deco_VHSCollection_C',
    'Deco_Rug_BASE_C',

    # Equipment
    'SnackShelf_Shelf_A_01_C',
    'BP_Television_Cart_C',
    'ClearanceBin_Base_C',
    'Fridge_Base_C',
    'CandyDispense_01_C',
    'Arcade_A_C',
    'Arcade_B_C',
    'Arcade_C_C',
    'ToysShelf_Base_C',
    'Pinball-Machine_A_C',
    'ConcessionsShelf_Base_C',
    'BP_Television_BASE_C',

    # Shelves
    'Shelf_Movie_4Row_01_C',
    'Shelf_Movie_4Row_02_C',
    'Shelf_Movie_5Row_01_C',
    'Shelf_Movie_5Row_02_C',
    'Shelf_Movie_6Row_01_C',
    'Shelf_Movie_6Row_02_C',
    'Shelf_NewMovie_4Row_02_C',
    'Shelf_NewMovie_5Row_02_C',
    'Shelf_NewMovie_6Row_02_C',
}


def is_included_class(cls):
    return cls in INCLUDED_CLASSES
ARRAYS = ['Shelve', 'Snack Shelf', 'Candy Dispenser']


def wf(s):
    raw = s.encode('utf-8') + b'\0'
    return struct.pack('<i', len(raw)) + raw


def read_fstring(data, pos):
    n = struct.unpack_from('<i', data, pos)[0]; pos += 4
    if n == 0: return '', pos
    if n > 0:
        raw = data[pos:pos+n]; pos += n
        if raw.endswith(b'\0'): raw = raw[:-1]
        return raw.decode('utf-8', errors='replace'), pos
    raw = data[pos:pos+(-n)*2]; pos += (-n)*2
    if raw.endswith(b'\0\0'): raw = raw[:-2]
    return raw.decode('utf-16le', errors='replace'), pos


def quat_to_euler(q):
    x, y, z, w = q
    sinr = 2*(w*x + y*z); cosr = 1-2*(x*x+y*y)
    roll = math.degrees(math.atan2(sinr, cosr))
    sinp = 2*(w*y-z*x)
    pitch = math.degrees(math.copysign(math.pi/2, sinp)) if abs(sinp)>=1 else math.degrees(math.asin(sinp))
    siny = 2*(w*z+x*y); cosy = 1-2*(y*y+z*z)
    yaw = math.degrees(math.atan2(siny, cosy))
    return [pitch, yaw, roll]


def quat_from_euler(pitch, yaw, roll):
    p=math.radians(pitch)*.5; y=math.radians(yaw)*.5; r=math.radians(roll)*.5
    sp,cp=math.sin(p),math.cos(p); sy,cy=math.sin(y),math.cos(y); sr,cr=math.sin(r),math.cos(r)
    return [sr*cp*cy-cr*sp*sy, cr*sp*cy+sr*cp*sy, cr*cp*sy-sr*sp*cy, cr*cp*cy+sr*sp*sy]


def parse_transform_payload(data, pos, size):
    end = pos + size; q = pos + 1; result = {}
    while q < end:
        name, q2 = read_fstring(data, q)
        if name == 'None': break
        typ, q2 = read_fstring(data, q2); q2 += 4
        struct_name, q2 = read_fstring(data, q2); q2 += 4
        _, q2 = read_fstring(data, q2); q2 += 4
        field_size = struct.unpack_from('<i', data, q2)[0]; q2 += 4
        value = q2 + 1
        if struct_name == 'Quat' and field_size >= 32 and name == 'Rotation':
            result['rotation_quat'] = list(struct.unpack_from('<4d', data, value))
        elif struct_name == 'Vector' and field_size >= 24:
            v = list(struct.unpack_from('<3d', data, value))
            if name == 'Translation': result['location'] = v
            elif name == 'Scale3D': result['scale'] = v
        q = q2 + 1 + field_size
    if not all(k in result for k in ('location','rotation_quat','scale')): return None
    result['rotation_euler'] = quat_to_euler(result['rotation_quat'])
    return result


def extract_element_transform(elem):
    p=elem.find(b'Transform_')
    if p<4: return None
    p-=4
    _,p=read_fstring(elem,p); typ,p=read_fstring(elem,p)
    if typ!='StructProperty': return None
    p+=4; sname,p=read_fstring(elem,p)
    if sname!='Transform': return None
    p+=4; _,p=read_fstring(elem,p); p+=4
    size=struct.unpack_from('<i',elem,p)[0]; p+=4
    return parse_transform_payload(elem,p,size)


def _read_named_int(data, aliases):
    for alias in aliases:
        marker = wf(alias)
        start = 0
        while True:
            at = data.find(marker, start)
            if at < 0:
                break
            p = at + len(marker)
            try:
                typ, p = read_fstring(data, p)
                if typ == 'IntProperty':
                    p += 4
                    size = struct.unpack_from('<i', data, p)[0]; p += 4
                    if size >= 4:
                        return struct.unpack_from('<i', data, p + 1)[0]
                elif typ == 'Int64Property':
                    p += 4
                    size = struct.unpack_from('<i', data, p)[0]; p += 4
                    if size >= 8:
                        return struct.unpack_from('<q', data, p + 1)[0]
            except (struct.error, UnicodeError, ValueError):
                pass
            start = at + 1
    return None


def _read_datetime_ticks(data, aliases):
    for alias in aliases:
        marker = wf(alias)
        start = 0
        while True:
            at = data.find(marker, start)
            if at < 0:
                break
            p = at + len(marker)
            try:
                typ, p = read_fstring(data, p)
                if typ != 'StructProperty':
                    start = at + 1
                    continue

                p += 4  # ArrayIndex
                struct_name, p = read_fstring(data, p)
                if struct_name != 'DateTime':
                    start = at + 1
                    continue

                p += 4  # struct guid/presence
                _, p = read_fstring(data, p)  # /Script/CoreUObject
                p += 4  # struct GUID
                payload_size = struct.unpack_from('<i', data, p)[0]
                p += 4

                # DateTime uses one leading serialization byte followed by
                # the eight-byte FDateTime tick value.
                if payload_size >= 8 and p + 9 <= len(data):
                    return struct.unpack_from('<q', data, p + 1)[0]
            except (struct.error, UnicodeError, ValueError):
                pass
            start = at + 1
    return None


def _ticks_to_string(ticks, date_only=False):
    if ticks is None:
        return None
    try:
        import datetime
        dt = datetime.datetime(1, 1, 1) + datetime.timedelta(microseconds=ticks / 10)
        return dt.strftime('%Y-%m-%d' if date_only else '%Y-%m-%d %H:%M')
    except (OverflowError, ValueError, OSError):
        return None



def _read_named_text(data, aliases):
    """Read a human-readable Unreal TextProperty value."""
    for alias in aliases:
        marker = wf(alias)
        start = 0
        while True:
            at = data.find(marker, start)
            if at < 0:
                break
            p = at + len(marker)
            candidates = []
            try:
                typ, p = read_fstring(data, p)
                if typ != 'TextProperty':
                    start = at + 1
                    continue

                p += 4  # ArrayIndex
                size = struct.unpack_from('<i', data, p)[0]
                p += 4
                end = min(p + max(size, 0), len(data))

                q = p
                while q + 4 <= end:
                    n = struct.unpack_from('<i', data, q)[0]
                    if 1 <= n <= 1024 and q + 4 + n <= len(data):
                        raw = data[q + 4:q + 4 + n]
                        if raw.endswith(b'\0'):
                            raw = raw[:-1]
                        try:
                            value = raw.decode('utf-8').strip()
                            if value and any(ch.isalpha() for ch in value):
                                # Reject GUID/hash-like strings such as
                                # BB194D4840C3D547E4A5488054DC54DD.
                                compact = value.replace('-', '').replace('_', '')
                                if len(compact) in (32, 36) and all(
                                    ch in '0123456789abcdefABCDEF' for ch in compact
                                ):
                                    q += 1
                                    continue

                                score = 0
                                if ' ' in value:
                                    score += 100
                                if len(value) <= 64:
                                    score += 10
                                if any(ch.islower() for ch in value):
                                    score += 5
                                if any(ch in value for ch in "'-&") or ' ' in value:
                                    score += 5
                                candidates.append((score, value))
                        except UnicodeDecodeError:
                            pass
                    q += 1

                if candidates:
                    candidates.sort(key=lambda item: (-item[0], len(item[1])))
                    return candidates[0][1]
            except (struct.error, UnicodeError, ValueError):
                pass
            start = at + 1
    return None

def _count_movie_objects(data):
    # Each saved movie entry carries the exact blueprint class token once.
    return data.count(b'videotape_C')


ROOM_NAMES = ("A", "B", "C", "D", "E", "F")

def extract_room_unlocks(data):
    """Read the six Store Expansion room unlock BoolProperties.
    The serialized records occur in A-F order and the BoolProperty value is
    stored 69 bytes after the 'Unlocked_5_' property-name start.
    """
    flags = []
    needle = b"Unlocked_5_"
    pos = 0
    while True:
        i = data.find(needle, pos)
        if i < 0:
            break
        value_pos = i + 69
        if value_pos >= len(data):
            break
        flags.append(data[value_pos] == 0x10)
        pos = i + len(needle)

    if len(flags) != 6:
        raise ValueError(f"Expected 6 Store Expansion room unlock flags, found {len(flags)}.")

    return {name: bool(flags[idx]) for idx, name in enumerate(ROOM_NAMES)}

def unlocked_room_names(room_unlocks):
    return [name for name in ROOM_NAMES if room_unlocks.get(name) is True]


STORE_UPGRADE_MARKERS = (
    (b'PopcornMachine', 'Popcorn Machine'),
    (b'Repair Station', 'Repair Desk'),
    (b'Sidewalk Sign', 'Sidewalk Sign'),
    (b'SlushMachine', 'Slush Machine'),
    (b'CottonCandyMachine', 'Cotton Candy Machine'),
)

def extract_store_upgrades(data):
    """Return the store upgrades actually unlocked in the save.

    These upgrades are represented by their saved upgrade records.  The
    marker names below are based on the game's serialized save records and
    are intentionally kept separate from the Store Expansion room unlocks.
    System Rentals is stored as an Unlock_18_/Bought_20_ pair in the
    ConsoleRental save block.
    """
    upgrades = []
    for marker, display_name in STORE_UPGRADE_MARKERS:
        if marker in data:
            upgrades.append(display_name)

    if b'Unlock_18_A201B27B488AA20F414A92889751108A' in data:
        upgrades.append('System Rentals')

    return upgrades

def extract_save_metadata(save_path):
    data = Path(save_path).read_bytes()
    if not data.startswith(b'GVAS'):
        raise ValueError('This is not a GVAS save file.')

    level = _read_named_int(
        data, ['Level_4_D665A6394BEDE281DFD626929A94188A'])
    money64 = _read_named_int(
        data, ['Money64_14_4E3C8A914A3F4058F90C30BD533FEF7E'])
    current_ticks = _read_datetime_ticks(
        data, ['CurrentDate_8_5789E10A46E7257D64CAEE994FE07344'])
    saved_ticks = _read_datetime_ticks(data, ['Saved Date Time'])

    # These are all IntProperty values stored in the save's main game-state block.
    # Keep the exact generated property names/IDs because Unreal serializes the
    # display names together with their generated suffixes.
    return {
        'shop_name': _read_named_text(
            data, ['Name_18_38CE29144D5E2B7E9AA3ED927CEFED81']),
        'total_movies': _count_movie_objects(data),
        'level': level,
        'experience': _read_named_int(data, ['Experience_6_F66B53AE4370A4DF189F7CA1520F61D0']),
        'store_value': _read_named_int(data, ['StoreValue_9_B0D609D4496095B4BB5DD0B9FB214A7E']),
        'lifetime_experience': _read_named_int(data, ['LifetimeExperience_15_2852A2D248073E93C050BB82859B2630']),
        'money64': money64,
        'game_day': _read_named_int(data, ['Day_2_7930E3D540CA025C11121088937EC833']),
        'global_footsteps': _read_named_int(data, ['GlobalNumberofFootstep_2_1CE22DF7472081B4637FB99538E95C1B']),
        'global_clients_served': _read_named_int(data, ['GlobalNumberofClientServed_6_64BFBC624FE077D14B541D824CA581C4']),
        'daily_movie_returns': _read_named_int(data, ['DailyNumberofmovieReturn_24_DE7255144CEBB8AAFD07CDBE34D8CD75']),
        'daily_xp': _read_named_int(data, ['DailyXPDaily_40_E130CBF74137F6FD069FE091A4E1EE27']),
        'daily_staff_spending': _read_named_int(data, ['DailySpendingStaff_49_83A82D51464F34B3F2402B86EAB060D7']),
        'game_date': _ticks_to_string(current_ticks, True),
        'last_played': _ticks_to_string(saved_ticks, False),
        'room_unlocks': extract_room_unlocks(data),
        'store_upgrades': extract_store_upgrades(data)
    }


def discover_object_arrays(data):
    """Discover serialized ArrayProperty containers that hold placed objects.

    UE/FArchive strings are length-prefixed.  We cannot calculate the array
    name prefix from the position of the ArrayProperty type alone, so walk
    backwards through the valid string-length candidates and then parse the
    complete ArrayProperty header.
    """
    found = []
    seen = set()
    type_marker = wf('ArrayProperty')
    pos = 0

    while True:
        typ_pos = data.find(type_marker, pos)
        if typ_pos < 4:
            break

        candidate = None

        # wf(array_name) is immediately before wf('ArrayProperty'):
        #   [int32 byte_length][UTF-8 name + NUL]
        # Find a length prefix whose payload ends exactly at typ_pos.
        low = max(0, typ_pos - 4 - 512)
        high = typ_pos - 4
        for prefix_pos in range(low, high + 1):
            try:
                n = struct.unpack_from('<i', data, prefix_pos)[0]
            except struct.error:
                continue
            if n <= 0 or n > 512:
                continue
            if prefix_pos + 4 + n != typ_pos:
                continue
            raw = data[prefix_pos + 4:typ_pos]
            if not raw.endswith(b'\0'):
                continue
            try:
                name = raw[:-1].decode('utf-8')
            except UnicodeDecodeError:
                continue
            if not name or name in seen:
                continue
            candidate = (prefix_pos, name)
            break

        if candidate is None:
            pos = typ_pos + 1
            continue

        array_start, array_name = candidate
        try:
            info = array_info(data, array_start)
            elements = split_elements(data, info)
        except (ValueError, struct.error, UnicodeError, IndexError):
            pos = typ_pos + 1
            continue

        # A valid object array has at least one element containing a VideoStore
        # asset reference and a usable Transform.  This catches regular
        # furniture and decorations without hard-coding their array names.
        has_object = False
        for elem in elements:
            got = element_class(elem)
            if not got:
                continue
            cls, _asset = got
            if not cls.endswith('_C'):
                continue
            try:
                if extract_element_transform(elem):
                    has_object = True
                    break
            except (ValueError, struct.error, UnicodeError):
                continue

        if has_object:
            # SaveGame_VHS has a ConsoleRent-Type IntProperty immediately
            # before the real Snack Shelf ArrayProperty.  A backwards scan can
            # therefore select a byte-aligned false string-length candidate
            # that includes that preceding property in the apparent name.
            # Normalize it to the real game array name so the importer never
            # writes a malformed property name back into the save.
            if 'Snack Shelf' in array_name:
                array_name = 'Snack Shelf'
            if array_name not in seen:
                seen.add(array_name)
                found.append(array_name)

        pos = typ_pos + 1

    return found



def extract_furniture(save_path):
    data=Path(save_path).read_bytes()
    if not data.startswith(b'GVAS'): raise ValueError('This is not a GVAS save file.')
    results=[]; seen=set()
    for array_name in discover_object_arrays(data):
        st=data.find(wf(array_name)+wf('ArrayProperty'))
        if st<0: continue
        info=array_info(data,st)
        for elem in split_elements(data,info):
            got=element_class(elem)
            if not got: continue
            cls,asset=got
            if not is_included_class(cls) or not cls.endswith('_C'): continue
            try:
                tr=extract_element_transform(elem)
            except (ValueError, struct.error, UnicodeError):
                continue
            if not tr: continue
            key=(cls,asset,tuple(round(v,5) for v in tr['location']),tuple(round(v,8) for v in tr['rotation_quat']))
            if key in seen: continue
            seen.add(key)
            results.append({'class':cls,'asset':asset,'array':array_name,**tr})
    return results



def inspect_save(save_path):
    data = Path(save_path).read_bytes()
    if not data.startswith(b'GVAS'):
        raise ValueError('This is not a GVAS save file.')

    objects = []
    seen = set()
    for array_name in discover_object_arrays(data):
        st = data.find(wf(array_name) + wf('ArrayProperty'))
        if st < 0:
            continue
        try:
            info = array_info(data, st)
            elements = split_elements(data, info)
        except (ValueError, struct.error, UnicodeError):
            continue

        for elem in elements:
            got = element_class(elem)
            if not got:
                continue
            cls, asset = got
            try:
                tr = extract_element_transform(elem)
            except (ValueError, struct.error, UnicodeError):
                tr = None

            item = {
                'array': array_name,
                'class': cls,
                'asset': asset,
                'excluded': not is_included_class(cls)
            }
            if tr:
                item['location'] = tr.get('location')
                item['rotation'] = tr.get('rotation_quat')
                item['scale'] = tr.get('scale')
            objects.append(item)

    # The furniture arrays contain the placed shop objects, but movies are
    # serialized in their own save-game records rather than in those arrays.
    # The original inspector counted both, plus excluded placed objects.
    # Add those records so "All Objects" represents the complete object count.
    def append_asset_occurrences(class_name, array_name, excluded_flag):
        needle = class_name.encode('utf-8') + b'\x00'
        pos = 0
        while True:
            p = data.find(needle, pos)
            if p < 0:
                break
            asset_map = {
                'videotape_C': '/Game/VideoStore/asset/prop/vhs/videotape.videotape_C',
                'Storage_Box_C': '/Game/VideoStore/asset/prop/storage/Storage_Box.Storage_Box_C',
                'VHSPlayer_C': '/Game/VideoStore/asset/prop/vhs/VHSPlayer.VHSPlayer_C'
            }
            asset = asset_map.get(class_name, class_name)
            objects.append({
                'array': array_name,
                'class': class_name,
                'asset': asset,
                'excluded': excluded_flag
            })
            pos = p + len(needle)

    append_asset_occurrences('videotape_C', 'Movies', True)
    for cls in ('Storage_Box_C', 'VHSPlayer_C'):
        append_asset_occurrences(cls, 'Excluded Objects', True)

    # Television Base objects live in the dedicated Televison MapProperty.
    # Surface them in All Objects/Equipment and apply the allow-list normally.
    tv_count = _count_televisions(data)
    for _ in range(tv_count):
        objects.append({
            'array': 'Televison',
            'class': 'BP_Television_BASE_C',
            'asset': '/Game/VideoStore/asset/prop/TV/BP_Television_BASE.BP_Television_BASE_C',
            'excluded': not is_included_class('BP_Television_BASE_C')
        })

    furniture = sum(1 for o in objects if not o['excluded'] and o['class'].endswith('_C'))
    excluded = sum(1 for o in objects if o['excluded'])
    movies = sum(1 for o in objects if o['class'].lower() == 'videotape_c')

    meta = extract_save_metadata(save_path)
    meta.update({
        'file_name': Path(save_path).name,
        'file_size': len(data),
        'object_count': len(objects),
        'furniture_count': furniture,
        'excluded_count': excluded,
        'movie_count': meta.get('total_movies', movies)
    })

    return {'metadata': meta, 'objects': objects}

def collect_templates(save_path):
    data=Path(save_path).read_bytes(); templates={}; headers={}
    for a in discover_object_arrays(data):
        st=data.find(wf(a)+wf('ArrayProperty'))
        if st<0: continue
        info=array_info(data,st)
        headers[a]=base64.b64encode(data[info['start']:info['size_pos']]).decode('ascii')
        for e in split_elements(data,info):
            got=element_class(e)
            if got and is_included_class(got[0]):
                templates.setdefault(got[0], base64.b64encode(e).decode('ascii'))
    return templates, headers


def extract_furniture_with_templates(save_path):
    """Extract included furniture and preserve each exact serialized instance.

    A class-level template is not sufficient for Unreal objects: two instances
    of the same class can have different serialized arrays/state (for example,
    shelves with different slot/ownership data).  Keeping the exact source
    element prevents those per-instance properties from being lost during a
    blueprint round-trip.
    """
    data=Path(save_path).read_bytes()
    results=[]
    for array_name in discover_object_arrays(data):
        st=data.find(wf(array_name)+wf('ArrayProperty'))
        if st<0: continue
        info=array_info(data,st)
        for elem in split_elements(data,info):
            got=element_class(elem)
            if not got: continue
            cls,asset=got
            if not is_included_class(cls) or not cls.endswith('_C'): continue
            try:
                tr=extract_element_transform(elem)
            except (ValueError, struct.error, UnicodeError):
                continue
            if not tr: continue
            item={'class':cls,'asset':asset,'array':array_name,**tr}
            item['_template']=base64.b64encode(elem).decode('ascii')
            results.append(item)
    return results



def _wall_style_positions(data):
    """Return the byte offsets of Wall Mesh/Skin integer values.

    Retro Rewind stores all 102 wall/floor/ceiling style entries in the Wall
    map. The serialized field names are stable, and the Mesh/Skin pairs occur
    once per entry. We deliberately restrict this scan to the contiguous Wall
    style region so furniture/object Mesh fields are never touched.
    """
    wall_start = data.find(b'\x05\x00\x00\x00Wall\x00\x0c\x00\x00\x00MapProperty')
    if wall_start < 0:
        raise ValueError('The save does not contain a Wall style map.')

    # In the current save format ConsoleRental is the first top-level property
    # after the Wall map. The style fields are all before that boundary.
    wall_end = data.find(b'\x0e\x00\x00\x00ConsoleRental\x00', wall_start + 1)
    if wall_end < 0:
        raise ValueError('Could not locate the end of the Wall style map.')

    positions = {'mesh': [], 'skin': []}
    for field, key in ((b'Mesh_4_', 'mesh'), (b'Skin_5_', 'skin')):
        pos = wall_start
        while True:
            pos = data.find(field, pos, wall_end)
            if pos < 0:
                break
            # The field is an FName: 4-byte length immediately precedes the
            # UTF-8 name. After the name's NUL comes the IntProperty header:
            # type FName, array index, payload size, property-guid flag, value.
            name_end = data.find(b'\x00', pos, wall_end)
            if name_end < 0:
                raise ValueError(f'Malformed Wall {key} field.')
            q = name_end + 1
            type_len = struct.unpack_from('<i', data, q)[0]
            q += 4 + type_len
            q += 4  # array index
            size = struct.unpack_from('<i', data, q)[0]
            q += 4
            if size != 4:
                raise ValueError(f'Unexpected Wall {key} field size: {size}')
            q += 1  # property GUID-present flag
            positions[key].append(q)
            pos = q + 4
    if len(positions['mesh']) != len(positions['skin']):
        raise ValueError('Wall Mesh/Skin entry counts do not match.')
    return positions


# GUID-matched store-style transfer supports stores with different room counts.
def _wall_style_entries(data):
    """Return wall/floor/ceiling style entries keyed by their stable entry GUID."""
    wall_start = data.find(b'\x05\x00\x00\x00Wall\x00\x0c\x00\x00\x00MapProperty')
    if wall_start < 0:
        raise ValueError('The save does not contain a Wall style map.')
    wall_end = data.find(b'\x0e\x00\x00\x00ConsoleRental\x00', wall_start + 1)
    if wall_end < 0:
        raise ValueError('Could not locate the end of the Wall style map.')

    entries=[]
    pos=wall_start
    while True:
        id_pos=data.find(b'ID_',pos,wall_end)
        if id_pos<0: break
        name_end=data.find(b'\x00',id_pos,wall_end)
        if name_end<0: break
        guid_field=data.find(b'\x05\x00\x00\x00Guid\x00',name_end+1,min(wall_end,name_end+120))
        if guid_field<0:
            pos=name_end+1; continue
        core_marker=b'/Script/CoreUObject\x00'
        core_pos=data.find(core_marker,guid_field,min(wall_end,guid_field+160))
        if core_pos<0:
            pos=name_end+1; continue
        guid_start=core_pos+len(core_marker)+9
        guid=data[guid_start:guid_start+16]
        if len(guid)!=16:
            pos=name_end+1; continue
        mesh_pos=data.find(b'Mesh_4_',guid_start+16,min(wall_end,guid_start+400))
        skin_pos=data.find(b'Skin_5_',mesh_pos+1,min(wall_end,mesh_pos+250)) if mesh_pos>=0 else -1
        if mesh_pos<0 or skin_pos<0:
            pos=name_end+1; continue
        def val(field_pos):
            field_end=data.find(b'\x00',field_pos,wall_end)
            q=field_end+1
            typelen=struct.unpack_from('<i',data,q)[0]
            q += 4+typelen+4
            size=struct.unpack_from('<i',data,q)[0]
            q += 4
            if size!=4: raise ValueError('Unexpected Wall style field size.')
            q += 1
            return struct.unpack_from('<i',data,q)[0]
        try:
            mesh=val(mesh_pos); skin=val(skin_pos)
        except (struct.error,ValueError):
            pos=name_end+1; continue
        entries.append({'guid':guid.hex().upper(),'mesh':mesh,'skin':skin})
        pos=skin_pos+1
    return entries


def extract_store_style(save_path):
    data=Path(save_path).read_bytes()
    entries=_wall_style_entries(data)
    return {'format':'RetroRewindStoreStyle','version':'1.0.0','entry_count':len(entries),'entries':entries}


def apply_store_style(source_data,target_data,style):
    """Apply Wall Mesh/Skin values by stable entry GUID.

    Source and target stores may have different numbers of style entries.
    Matching GUIDs are updated; target-only entries are left untouched.
    """
    if not style:
        return target_data,0
    target_entries=_wall_style_entries(target_data)
    if 'entries' in style:
        source_entries=style.get('entries') or []
        target_order=[e['guid'] for e in target_entries]
        target_pos=_wall_style_positions(target_data)
        mesh_pos=dict(zip(target_order,target_pos['mesh']))
        skin_pos=dict(zip(target_order,target_pos['skin']))
        out=bytearray(target_data)
        applied=0
        for e in source_entries:
            if not isinstance(e,dict): continue
            guid=str(e.get('guid','')).upper()
            if guid not in mesh_pos: continue
            if 'mesh' not in e or 'skin' not in e:
                raise ValueError('Blueprint store style entry is incomplete.')
            struct.pack_into('<i',out,mesh_pos[guid],int(e['mesh']))
            struct.pack_into('<i',out,skin_pos[guid],int(e['skin']))
            applied+=1
        return bytes(out),applied

    # Backwards compatibility with version-1 blueprints. If the source save
    # is available (it always is during an import), use its stable GUID order
    # to upgrade the old positional arrays into GUID-matched style entries.
    src_count=int(style.get('entry_count',0)); mesh=style.get('mesh') or []; skin=style.get('skin') or []
    if src_count!=len(mesh) or src_count!=len(skin):
        raise ValueError('Blueprint store style data is incomplete.')
    try:
        source_entries=_wall_style_entries(source_data)
    except ValueError:
        source_entries=[]
    if len(source_entries)==src_count:
        target_order=[e['guid'] for e in target_entries]
        target_pos=_wall_style_positions(target_data)
        mesh_pos=dict(zip(target_order,target_pos['mesh']))
        skin_pos=dict(zip(target_order,target_pos['skin']))
        out=bytearray(target_data)
        applied=0
        for i,e in enumerate(source_entries):
            guid=e['guid']
            if guid not in mesh_pos: continue
            struct.pack_into('<i',out,mesh_pos[guid],int(mesh[i]))
            struct.pack_into('<i',out,skin_pos[guid],int(skin[i]))
            applied+=1
        return bytes(out),applied

    # Last-resort legacy behavior for a blueprint whose source save cannot be
    # inspected and whose target layout has the exact same entry count.
    target_pos=_wall_style_positions(target_data)
    if len(target_pos['mesh'])!=src_count or len(target_pos['skin'])!=src_count:
        raise ValueError(f'Target save has {len(target_pos["mesh"])} store-style entries; the blueprint contains {src_count}. The store layout does not match.')
    out=bytearray(target_data)
    for i,p in enumerate(target_pos['mesh']): struct.pack_into('<i',out,p,int(mesh[i]))
    for i,p in enumerate(target_pos['skin']): struct.pack_into('<i',out,p,int(skin[i]))
    return bytes(out),src_count



def _television_property_span(data):
    """Return the serialized Televison MapProperty span."""
    start = data.find(wf('Televison') + wf('MapProperty'))
    if start < 0:
        return None
    candidates = []
    for name in ('Cartridge', 'Shelve', 'Wall', 'ConsoleRental'):
        p = data.find(wf(name), start + 1)
        if p >= 0:
            candidates.append(p)
    if not candidates:
        raise ValueError('Could not locate the end of the Televison map.')
    return start, min(candidates)


def _extract_television_map(data):
    span = _television_property_span(data)
    if not span:
        return None
    return base64.b64encode(data[span[0]:span[1]]).decode('ascii')


def _count_televisions(data):
    span = _television_property_span(data)
    if not span:
        return 0
    start, end = span
    return data[start:end].count(b'BP_Television_BASE_C\x00')


def export_blueprint(save_path, output_path, export_furniture=True, export_store_style=True):
    items = extract_furniture_with_templates(save_path) if export_furniture else []
    templates, headers = collect_templates(save_path) if export_furniture else ({}, {})
    meta = extract_save_metadata(save_path)
    style = extract_store_style(save_path) if export_store_style else None
    tv_map = _extract_television_map(Path(save_path).read_bytes()) if export_furniture else None
    tv_count = _count_televisions(Path(save_path).read_bytes()) if tv_map else 0
    doc = {
        'format': 'RetroRewindStoreBlueprint',
        'version': 2,
        'source_save': Path(save_path).name,
        'shop_name': meta.get('shop_name') or '',
        'read_only_export': True,
        'count': len(items),
        'export_furniture': bool(export_furniture),
        'export_store_style': bool(export_store_style),
        'furniture': items,
        # Kept for backwards compatibility with older blueprint readers.
        'templates': templates,
        'array_headers': headers,
        'room_unlocks': meta.get('room_unlocks', {}),
        'store_style': style,
        'television_map': tv_map,
    }
    Path(output_path).write_text(json.dumps(doc, indent=2), encoding='utf-8')
    return items

def blueprint_metadata(path):
    doc=json.loads(Path(path).read_text(encoding='utf-8'))
    if doc.get('format')!='RetroRewindStoreBlueprint':
        raise ValueError('Not a Retro Rewind Store Blueprint.')
    return {
        'shop_name': doc.get('shop_name') or '',
        'source_save': doc.get('source_save') or '',
        'count': int(doc.get('count', len(doc.get('furniture', [])))),
        'version': doc.get('version'),
        'room_unlocks': doc.get('room_unlocks')
    }


def array_info(data,start):
    p=start; name,p=read_fstring(data,p); typ,p=read_fstring(data,p)
    if typ!='ArrayProperty': raise ValueError(f'{name} is not ArrayProperty')
    array_index=struct.unpack_from('<i',data,p)[0]; p+=4
    inner,p=read_fstring(data,p)
    if inner!='StructProperty': raise ValueError(f'{name}: unexpected inner type {inner}')
    p+=4; struct_name,p=read_fstring(data,p); p+=4; struct_type,p=read_fstring(data,p); p+=4
    guid,p=read_fstring(data,p); p+=4
    size_pos=p; size=struct.unpack_from('<i',data,p)[0]; p+=4; payload=p; end=payload+size
    if end>len(data): raise ValueError(f'{name}: invalid array size')
    count=struct.unpack_from('<i',data,payload+1)[0]
    return {'name':name,'start':start,'size_pos':size_pos,'payload':payload,'end':end,'size':size,'count':count,'struct_name':struct_name,'struct_type':struct_type,'guid':guid,'array_index':array_index}


def split_elements(data,info):
    start=info['payload']+5; end=info['end']; count=info['count']
    if count==0:return []
    first,first_end=read_fstring(data,start); marker=data[start:first_end]
    positions=[]; q=start
    while True:
        z=data.find(marker,q,end)
        if z<0:break
        positions.append(z); q=z+1
    if len(positions)!=count: raise ValueError(f"{info['name']}: expected {count} elements, found {len(positions)}")
    return [data[positions[i]:positions[i+1] if i+1<len(positions) else end] for i in range(count)]


def element_class(elem):
    marker=b'/Game/VideoStore/asset/prop/'; p=elem.find(marker)
    if p<4:return None
    n=struct.unpack_from('<i',elem,p-4)[0]
    if n<=0 or p+n>len(elem):return None
    asset=elem[p:p+n-1].decode('utf-8','replace'); cls=asset.rsplit('.',1)[-1] if '.' in asset else asset
    return cls,asset


def _randomize_instance_guids(elem):
    """Give a cloned object fresh instance GUIDs."""
    import uuid
    out=bytearray(elem)
    marker=wf('StructProperty')
    pos=0
    while True:
        at=out.find(marker,pos)
        if at<0: break
        try:
            q=at+len(marker)+4
            struct_name,q=read_fstring(out,q); q+=4
            _,q=read_fstring(out,q); q+=4
            size=struct.unpack_from('<i',out,q)[0]; q+=4
            value_start=q+1
            if struct_name=='Guid' and size==16 and value_start+16<=len(out):
                if bytes(out[value_start:value_start+16]) != bytes(16):
                    out[value_start:value_start+16]=uuid.uuid4().bytes
        except (struct.error,UnicodeError,ValueError): pass
        pos=at+1
    marker=wf('ArrayProperty'); pos=0
    while True:
        at=out.find(marker,pos)
        if at<0: break
        try:
            q=at+len(marker)+4
            inner,q=read_fstring(out,q); q+=4
            struct_name,q=read_fstring(out,q); q+=4
            _,q=read_fstring(out,q); q+=4
            size=struct.unpack_from('<i',out,q)[0]; q+=4
            payload=q
            if inner=='StructProperty' and struct_name=='Guid' and payload+size+1<=len(out):
                count=struct.unpack_from('<i',out,payload+1)[0]; start_value=payload+5
                if 0<=count<=10000 and start_value+16*count<=payload+size+1:
                    for i in range(count):
                        atv=start_value+i*16
                        if bytes(out[atv:atv+16]) != bytes(16):
                            out[atv:atv+16]=uuid.uuid4().bytes
        except (struct.error,UnicodeError,ValueError): pass
        pos=at+1
    return bytes(out)

def patch_transform_no_guid(elem,item):
    """Patch only the transform; preserve every other per-instance field.

    The serialized element may contain GUID arrays that are referenced by other
    state in the save.  Those must remain exactly as authored in the source
    instance.
    """
    p=elem.find(b'Transform_')
    if p<4: raise ValueError('Template element has no Transform_* property')
    p-=4; _,p=read_fstring(elem,p); typ,p=read_fstring(elem,p)
    if typ!='StructProperty': raise ValueError('Unexpected Transform property type')
    p+=4; sname,p=read_fstring(elem,p)
    if sname!='Transform': raise ValueError('Unexpected transform struct name')
    p+=4; _,p=read_fstring(elem,p); p+=4
    size=struct.unpack_from('<i',elem,p)[0]; p+=4; payload=p; end=p+size
    out=bytearray(elem); q=payload+1
    while q<end:
        fname,q2=read_fstring(elem,q)
        if fname=='None': break
        _,q2=read_fstring(elem,q2); q2+=4; fsname,q2=read_fstring(elem,q2); q2+=4; _,q2=read_fstring(elem,q2); q2+=4
        fsize=struct.unpack_from('<i',elem,q2)[0]; q2+=4; data_start=q2+1
        if fsname=='Quat' and fsize>=32 and fname=='Rotation':
            qv=item.get('rotation_quat') or quat_from_euler(*item.get('rotation_euler',[0,0,0])); struct.pack_into('<4d',out,data_start,*qv[:4])
        elif fsname=='Vector' and fsize>=24:
            vals=item['location'] if fname=='Translation' else item.get('scale',[1,1,1]) if fname=='Scale3D' else None
            if vals is not None: struct.pack_into('<3d',out,data_start,*vals[:3])
        q=q2+1+fsize
    return bytes(out)


def patch_transform(elem,item):
    p=elem.find(b'Transform_')
    if p<4: raise ValueError('Template element has no Transform_* property')
    p-=4; _,p=read_fstring(elem,p); typ,p=read_fstring(elem,p)
    if typ!='StructProperty': raise ValueError('Unexpected Transform property type')
    p+=4; sname,p=read_fstring(elem,p)
    if sname!='Transform': raise ValueError('Unexpected transform struct name')
    p+=4; _,p=read_fstring(elem,p); p+=4
    size=struct.unpack_from('<i',elem,p)[0]; p+=4; payload=p; end=p+size
    out=bytearray(elem); q=payload+1
    while q<end:
        fname,q2=read_fstring(elem,q)
        if fname=='None': break
        _,q2=read_fstring(elem,q2); q2+=4; fsname,q2=read_fstring(elem,q2); q2+=4; _,q2=read_fstring(elem,q2); q2+=4
        fsize=struct.unpack_from('<i',elem,q2)[0]; q2+=4; data_start=q2+1
        if fsname=='Quat' and fsize>=32 and fname=='Rotation':
            qv=item.get('rotation_quat') or quat_from_euler(*item.get('rotation_euler',[0,0,0])); struct.pack_into('<4d',out,data_start,*qv[:4])
        elif fsname=='Vector' and fsize>=24:
            vals=item['location'] if fname=='Translation' else item.get('scale',[1,1,1]) if fname=='Scale3D' else None
            if vals is not None: struct.pack_into('<3d',out,data_start,*vals[:3])
        q=q2+1+fsize
    return _randomize_instance_guids(bytes(out))

def build_array_property(source,info,elements):
    prefix=source[info['start']:info['size_pos']]
    payload=bytearray([0])+struct.pack('<i',len(elements))+b''.join(elements)
    # A tagged UE property is followed by a one-byte property-GUID flag.
    # Native saves use 00 here when no GUID follows. The old importer omitted
    # this byte, causing the next property to be parsed as part of the array.
    return prefix+struct.pack('<i',len(payload))+payload+b'\x00'


def build_array_property_from_prefix(prefix,elements):
    payload=bytearray([0])+struct.pack('<i',len(elements))+b''.join(elements)
    return prefix+struct.pack('<i',len(payload))+payload+b'\x00'


def load_blueprint(path):
    doc=json.loads(Path(path).read_text(encoding='utf-8'))
    if doc.get('format')!='RetroRewindStoreBlueprint': raise ValueError('Not a Retro Rewind Store Blueprint.')
    items=[x for x in doc.get('furniture',[]) if is_included_class(x.get('class', ''))]
    templates={k:base64.b64decode(v) for k,v in doc.get('templates',{}).items()}
    headers={k:base64.b64decode(v) for k,v in doc.get('array_headers',{}).items()}
    style=doc.get('store_style') if doc.get('export_store_style', bool(doc.get('store_style'))) else None
    television_map=base64.b64decode(doc['television_map']) if doc.get('television_map') else None
    return items, templates, headers, style, television_map


def find_blueprint_source(blueprint_path):
    """Return a nearby source save for legacy blueprints that lack templates."""
    try:
        bp=Path(blueprint_path)
        doc=json.loads(bp.read_text(encoding='utf-8'))
        name=doc.get('source_save')
        if not name:
            return ''
        candidate=(bp.parent/name)
        if candidate.is_file() and candidate.suffix.lower()=='.sav':
            return str(candidate)
    except Exception:
        pass
    return ''


def import_blueprint(source_save,target_save,blueprint_path,output_path,replace_existing=True,import_furniture=True,import_store_style=True):
    """Import furniture using the original v8 template strategy.

    The important v8 behavior is that the serialized object templates come
    directly from the SOURCE save. Portable templates embedded in a blueprint
    are useful as a fallback, but must not replace the source templates when a
    source save is available: the source save carries the exact object
    properties/metadata the game expects.
    """
    target=Path(target_save).read_bytes()
    if not target.startswith(b'GVAS'):
        raise ValueError('Target save must be GVAS.')

    items, portable_templates, portable_headers, store_style, television_map=load_blueprint(blueprint_path)
    # Import options: selectively apply furniture and/or store style.
    if not import_furniture:
        items=[]
        television_map=None
    if not import_store_style:
        store_style=None

    source=Path(source_save).read_bytes() if source_save else b''
    if source_save:
        if not source.startswith(b'GVAS'):
            raise ValueError('Source save must be GVAS.')

    # v8: collect templates directly from the source save by class.
    # Prefer these over blueprint-embedded templates.
    templates_by_class={}
    source_arrays=[]
    if source:
        source_arrays=discover_object_arrays(source)

        # Keep the original transfer's three known furniture containers first,
        # while still allowing the modern exporter to carry additional arrays.
        template_arrays=[a for a in ARRAYS if a in source_arrays]
        template_arrays += [a for a in source_arrays if a not in template_arrays]

        for a in template_arrays:
            st=source.find(wf(a)+wf('ArrayProperty'))
            if st<0:
                continue
            try:
                for e in split_elements(source,array_info(source,st)):
                    got=element_class(e)
                    if got and is_included_class(got[0]) and got[0] not in templates_by_class:
                        templates_by_class[got[0]]=e
            except (ValueError, struct.error, UnicodeError):
                continue

    # Portable templates are embedded in the blueprint specifically so it
    # remains usable when the current source save is missing objects or uses
    # a different set of serialized object arrays.  Prefer the exact source
    # save template whenever available, but fill any missing classes from the
    # portable blueprint template captured at export time.
    #
    # This is important for allow-listed objects: the source save may not
    # contain every class present in the blueprint (for example, an object
    # that was later removed from that save), but the blueprint still carries
    # the original serialized template needed to import it.
    for cls, template in portable_templates.items():
        templates_by_class.setdefault(cls, template)

    target_arrays=discover_object_arrays(target)
    all_arrays=[]
    for a in source_arrays + target_arrays + list(portable_headers.keys()) + [x.get('array') for x in items]:
        if a and a not in all_arrays:
            all_arrays.append(a)

    # Determine where each object belongs. Prefer the array recorded in the
    # blueprint when it is a real source/target object array; otherwise fall
    # back to the class location, matching the v8 importer.
    class_source_array={}
    if source:
        for a in source_arrays:
            st=source.find(wf(a)+wf('ArrayProperty'))
            if st<0:
                continue
            try:
                for e in split_elements(source,array_info(source,st)):
                    got=element_class(e)
                    if got and got[0] not in class_source_array:
                        class_source_array[got[0]]=a
            except Exception:
                continue

    # Legacy blueprints only stored one template per class.  When the source
    # save is available, build a signature index so we can recover the exact
    # serialized source instance for those blueprints too.
    source_templates_by_signature={}
    if source:
        for a in source_arrays:
            st=source.find(wf(a)+wf('ArrayProperty'))
            if st<0: continue
            try:
                for e in split_elements(source,array_info(source,st)):
                    got=element_class(e)
                    if not got or not is_included_class(got[0]):
                        continue
                    tr=extract_element_transform(e)
                    if not tr:
                        continue
                    sig=(got[0], tuple(round(v,5) for v in tr['location']),
                         tuple(round(v,8) for v in tr['rotation_quat']))
                    source_templates_by_signature.setdefault(sig,e)
            except (ValueError, struct.error, UnicodeError):
                continue

    built={a:[] for a in all_arrays}
    missing=[]

    for item in items:
        cls=item['class']

        chosen=item.get('array')
        if chosen not in built:
            chosen=class_source_array.get(cls)

        if chosen not in built:
            # Preserve v8's class-name fallbacks for the known special cases.
            if cls.startswith('SnackShelf_'):
                chosen='Snack Shelf'
            elif cls.startswith('CandyDispense_'):
                chosen='Candy Dispenser'
            else:
                chosen='Shelve'

        # New v6 blueprints carry the exact serialized instance.  This is the
        # authoritative template and preserves per-instance arrays/GUIDs.
        t=None
        encoded=item.get('_template')
        if encoded:
            try:
                t=base64.b64decode(encoded)
            except (ValueError, TypeError):
                t=None

        # Recover exact source instances for older blueprints where possible.
        if t is None and source_templates_by_signature:
            sig=(cls, tuple(round(v,5) for v in item.get('location',[])),
                 tuple(round(v,8) for v in item.get('rotation_quat',[])))
            t=source_templates_by_signature.get(sig)

        # Final legacy fallback: class-level template.
        if t is None:
            t=templates_by_class.get(cls)

        if t is None:
            missing.append(cls)
            continue

        # IMPORTANT: do not randomize every nested GUID.  Shelves and similar
        # objects contain GUID arrays that are references to their serialized
        # per-instance state.  Changing those GUIDs without updating every
        # corresponding reference makes the game discard the entire array.
        built.setdefault(chosen,[]).append(patch_transform_no_guid(t,item))

    if missing:
        raise ValueError(
            'No source template for: ' + ', '.join(sorted(set(missing)))
        )

    props={}
    for a,els in built.items():
        if not els:
            continue

        # If the user chose to KEEP current furniture, preserve the target
        # array contents and append the transferred objects. This must happen
        # even when the source has the same array, otherwise the source array
        # silently replaces the entire target array.
        target_start=target.find(wf(a)+wf('ArrayProperty'))
        if not replace_existing and target_start>=0:
            target_info=array_info(target,target_start)
            existing=split_elements(target,target_info)
            props[a]=build_array_property(target,target_info,existing+els)
            continue

        # Otherwise replace the target array with the transferred objects,
        # using the complete SOURCE array metadata where available.
        source_start=source.find(wf(a)+wf('ArrayProperty')) if source else -1
        if source_start>=0:
            info=array_info(source,source_start)
            props[a]=build_array_property(source,info,els)
            continue

        # No source array: use a portable header only as a fallback.
        elif a in portable_headers:
            props[a]=build_array_property_from_prefix(portable_headers[a],els)
        else:
            raise ValueError(
                f'Target save is missing the {a} array and the source/blueprint '
                'does not contain an array header for it.'
            )

    # v8 placed the transferred object arrays at the END of the save, immediately
    # before the final top-level None.  Do that even when the target already has
    # stale/malformed copies of these arrays.  Replacing an array in-place can
    # leave a save that our inspector understands but the game does not load.
    result=target

    # Television Base objects are stored in the dedicated Televison MapProperty,
    # not in the normal furniture ArrayProperty containers.
    if television_map is not None:
        tv_span = _television_property_span(result)
        if tv_span:
            result = result[:tv_span[0]] + television_map + result[tv_span[1]:]
        else:
            insert_candidates = []
            for a in discover_object_arrays(result):
                p = result.find(wf(a) + wf('ArrayProperty'))
                if p >= 0:
                    insert_candidates.append(p)
            if not insert_candidates:
                raise ValueError('Target save does not contain a Televison map or a suitable insertion point.')
            p = min(insert_candidates)
            result = result[:p] + television_map + result[p:]

    # Store style is independent of furniture. Apply it after the furniture
    # arrays are assembled so the style-only toggle is honored even when no
    # furniture was exported.
    style_count = 0
    if store_style is not None:
        result, style_count = apply_store_style(source, result, store_style)

    # The game expects the transferred object arrays at the END of the save,
    # immediately before the final top-level None, with ConsoleRental between
    # the special SaveGame arrays and the normal furniture arrays.  This exact
    # structure was confirmed by a Save3 test that loads successfully:
    #
    #   Snack Shelf -> Candy Dispenser -> Arcade -> ConsoleRental ->
    #   Shelve -> PosterFrame -> Decoration -> None
    #
    # Keep unrelated top-level properties untouched. Remove only the arrays
    # we replace, then insert the replacement arrays around the existing
    # ConsoleRental property at the end of the save.
    transfer_names = {
        'Shelve', 'PosterFrame', 'Decoration',
        'Snack Shelf', 'Candy Dispenser', 'Arcade'
    }
    remove_ranges = []
    for a in transfer_names:
        st = result.find(wf(a) + wf('ArrayProperty'))
        if st < 0:
            continue
        try:
            info = array_info(result, st)
            end = info['end']
            if end < len(result) and result[end:end+1] == b'\x00':
                end += 1
            remove_ranges.append((st, end))
        except (ValueError, struct.error, UnicodeError):
            raise ValueError(f'Could not parse existing {a} array in target save.')

    for st, end in sorted(remove_ranges, reverse=True):
        result = result[:st] + result[end:]

    # The game expects the transferred object arrays at the END of the save,
    # immediately before the final top-level None.  ConsoleRental belongs
    # between the special SaveGame arrays and the normal furniture arrays.
    # This exact structure was confirmed by the known-good Save3 test:
    #
    #   Snack Shelf -> Candy Dispenser -> Arcade -> ConsoleRental ->
    #   Shelve -> PosterFrame -> Decoration -> None
    #
    # Remove the existing transfer arrays and the existing trailing
    # ConsoleRental block, then rebuild that final region in the exact order.
    marker = wf('None')
    final_none = result.rfind(marker)
    if final_none < 0:
        raise ValueError('Could not locate final top-level None.')

    console_start = result.rfind(wf('ConsoleRental'), 0, final_none)
    if console_start < 0:
        raise ValueError('Could not locate ConsoleRental before the final top-level None.')
    console_blob = result[console_start:final_none]

    # Remove the existing ConsoleRental block, preserving the final None.
    result = result[:console_start] + result[final_none:]

    special_blob = b''.join(
        props[a] for a in ('Snack Shelf', 'Candy Dispenser', 'Arcade') if a in props
    )
    normal_blob = b''.join(
        props[a] for a in ('Shelve', 'PosterFrame', 'Decoration') if a in props
    )

    final_none = result.rfind(marker)
    final_region = special_blob + console_blob + normal_blob
    result = result[:final_none] + final_region + result[final_none:]

    out=Path(output_path)
    # Backup creation remains the responsibility of the C# host.
    out.write_bytes(result)

    return {
        'items':len(items),
        'arrays':{k:len(v) for k,v in built.items() if v},
        'store_style_entries':style_count,
        'output':str(out),
        'size':len(result)
    }



def remove_all_furniture(save_path, output_path):
    data=Path(save_path).read_bytes()
    if not data.startswith(b'GVAS'):
        raise ValueError('Save must be GVAS.')
    result=data
    spans=[]
    # Only these six properties are Retro Rewind shop furniture.  Do not use
    # discover_object_arrays() here because that intentionally also detects
    # other object-bearing arrays such as Cartridge, Standees and Console base
    # which must remain untouched by Remove All Furniture.
    furniture_arrays = {
        'Shelve', 'PosterFrame', 'Decoration',
        'Snack Shelf', 'Candy Dispenser', 'Arcade'
    }
    for a in furniture_arrays:
        pos=data.find(wf(a)+wf('ArrayProperty'))
        if pos < 0:
            continue
        info=array_info(data,pos)
        spans.append((pos,info['end'],a,info))
    removed=0
    # Match the verified in-game test structure: completely remove the
    # furniture ArrayProperty nodes rather than leaving empty arrays behind.
    # The vanilla save has these properties absent, and this also avoids
    # triggering the transfer array-order validation on an empty property.
    for pos,end,a,info in sorted(spans,reverse=True):
        removed += len(split_elements(data,info))
        # array_info.end stops at the payload end; the tagged UE property
        # has one trailing property-GUID flag byte which must be removed too.
        remove_end = end + 1 if end < len(data) and data[end] == 0 else end
        result=result[:pos]+result[remove_end:]
    Path(output_path).write_bytes(result)
    return {'removed_objects':removed,'arrays':[x[2] for x in spans],'size':len(result)}


def restore_shop_style(target_save, output_path):
    target=Path(target_save).read_bytes()
    if not target.startswith(b'GVAS'):
        raise ValueError('The selected save must be GVAS.')

    # This is the verified untouched vanilla Retro Rewind save style captured
    # from Player_Save3(7).sav.  Only the Wall Mesh/Skin values are applied;
    # furniture and every other property in the selected save remain intact.
    vanilla_path=Path(__file__).with_name('vanilla_store_style.json')
    if not vanilla_path.exists():
        raise ValueError('The bundled vanilla shop style data is missing.')
    style=json.loads(vanilla_path.read_text(encoding='utf-8'))
    result,applied=apply_store_style(target,target,style)
    Path(output_path).write_bytes(result)
    return {'style_entries_restored':applied,'size':len(result)}

def check_array_order(save_path):
    data = Path(save_path).read_bytes()
    if not data.startswith(b'GVAS'):
        raise ValueError('This is not a GVAS save file.')

    expected = ['Snack Shelf', 'Candy Dispenser', 'Arcade', 'ConsoleRental',
                'Shelve', 'PosterFrame', 'Decoration']
    marker = wf('None')
    final_none = data.rfind(marker)
    if final_none < 0:
        raise ValueError('Could not locate final top-level None.')

    positions = {}
    # ArrayProperty names have an unambiguous property-type marker.
    for name in ('Snack Shelf', 'Candy Dispenser', 'Arcade', 'Shelve',
                 'PosterFrame', 'Decoration'):
        pos = data.rfind(wf(name) + wf('ArrayProperty'), 0, final_none)
        if pos >= 0:
            positions[name] = pos

    # ConsoleRental is a top-level property rather than an ArrayProperty, so
    # locate the final top-level occurrence immediately before the final None.
    console_pos = data.rfind(wf('ConsoleRental'), 0, final_none)
    if console_pos >= 0:
        positions['ConsoleRental'] = console_pos

    present = [name for name in expected if name in positions]
    ordered = sorted(present, key=lambda name: positions[name])
    ok = present == ordered

    return {
        'ok': ok,
        'expected_order': expected,
        'present_order': ordered,
        'error': '' if ok else (
            'Transfer object array ordering is invalid. Expected relative order: ' +
            ' -> '.join(expected) +
            '; found: ' + ' -> '.join(ordered))
    }


def transfer(source_save,target_save,output_path,replace_existing=True,transfer_furniture=True,transfer_store_style=True):
    with tempfile.TemporaryDirectory() as td:
        bp=Path(td)/'StoreBlueprint.rrblueprint'
        export_blueprint(source_save,bp,export_furniture=True,export_store_style=True)
        return import_blueprint(source_save,target_save,bp,output_path,replace_existing=replace_existing,import_furniture=transfer_furniture,import_store_style=transfer_store_style)


def main():
    p=argparse.ArgumentParser(description='Retro Rewind Store Transfer engine')
    sub=p.add_subparsers(dest='cmd',required=True)
    c=sub.add_parser('count'); c.add_argument('save')
    m=sub.add_parser('metadata'); m.add_argument('save')
    i=sub.add_parser('inspect'); i.add_argument('save')
    e=sub.add_parser('export'); e.add_argument('save'); e.add_argument('output'); e.add_argument('--no-furniture',action='store_true'); e.add_argument('--no-store-style',action='store_true')
    bm=sub.add_parser('blueprint_metadata'); bm.add_argument('blueprint')
    ru=sub.add_parser('room_unlocks'); ru.add_argument('save')
    ao=sub.add_parser('array_order'); ao.add_argument('save')
    i=sub.add_parser('import'); i.add_argument('source'); i.add_argument('target'); i.add_argument('blueprint'); i.add_argument('output'); i.add_argument('--keep-existing',action='store_true'); i.add_argument('--no-furniture',action='store_true'); i.add_argument('--no-store-style',action='store_true')
    t=sub.add_parser('transfer'); t.add_argument('source'); t.add_argument('target'); t.add_argument('output'); t.add_argument('--keep-existing',action='store_true'); t.add_argument('--no-furniture',action='store_true'); t.add_argument('--no-store-style',action='store_true')
    mf=sub.add_parser('remove-furniture'); mf.add_argument('source'); mf.add_argument('output')
    rs=sub.add_parser('restore-style'); rs.add_argument('target'); rs.add_argument('output')
    args=p.parse_args()
    if args.cmd=='count':
        items=extract_furniture(args.save)
        print(json.dumps({'ok':True,'operation':'count','count':len(items)}))
    elif args.cmd=='metadata':
        print(json.dumps({'ok':True,'operation':'metadata',**extract_save_metadata(args.save)}))
    elif args.cmd=='inspect':
        print(json.dumps({'ok':True,'operation':'inspect',**inspect_save(args.save)}, separators=(',', ':')))
    elif args.cmd=='export':
        items=export_blueprint(args.save,args.output,export_furniture=not args.no_furniture,export_store_style=not args.no_store_style); print(json.dumps({'ok':True,'operation':'export','count':len(items),'output':args.output,'export_furniture':not args.no_furniture,'export_store_style':not args.no_store_style}))
    elif args.cmd=='blueprint_metadata':
        print(json.dumps({'ok':True,'operation':'blueprint_metadata',**blueprint_metadata(args.blueprint)}))
    elif args.cmd=='array_order':
        print(json.dumps({'ok':True,'operation':'array_order',**check_array_order(args.save)}, separators=(',', ':')))
    elif args.cmd=='room_unlocks':
        data=Path(args.save).read_bytes()
        rooms=extract_room_unlocks(data)
        print(json.dumps({'ok':True,'operation':'room_unlocks','room_unlocks':rooms,'store_upgrades':extract_store_upgrades(data)}))
    elif args.cmd=='import':
        r=import_blueprint(
            args.source,
            args.target,
            args.blueprint,
            args.output,
            replace_existing=True,
            import_furniture=not args.no_furniture,
            import_store_style=not args.no_store_style
        )
        print(json.dumps({
            'ok':True,
            'operation':'import',
            **r,
            'import_furniture':not args.no_furniture,
            'import_store_style':not args.no_store_style,
            'replace_existing':True
        }))
    elif args.cmd=='remove-furniture':
        r=remove_all_furniture(args.source,args.output)
        print(json.dumps({'ok':True,'operation':'remove-furniture',**r}))
    elif args.cmd=='restore-style':
        r=restore_shop_style(args.target,args.output)
        print(json.dumps({'ok':True,'operation':'restore-style',**r}))
    else:
        r=transfer(
            args.source,
            args.target,
            args.output,
            replace_existing=True,
            transfer_furniture=not args.no_furniture,
            transfer_store_style=not args.no_store_style
        )
        print(json.dumps({
            'ok':True,
            'operation':'transfer',
            **r,
            'transfer_furniture':not args.no_furniture,
            'transfer_store_style':not args.no_store_style,
            'replace_existing':True
        }))

if __name__=='__main__':
    try: main()
    except Exception as e:
        print(json.dumps({'ok':False,'error':str(e)})); sys.exit(1)
