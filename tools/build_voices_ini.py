"""Makes PedestrianVoices.ini from the Excel table przechodnie_dzwieki.xlsx (sheet "Postacie").

Columns: A = sprite set, E = pool of the first reaction, F = pool of the second reaction, G = notes.
Several effects in one cell: one per line (or separated by commas). A note of the form "na uderzeniu losuj tez spomiedzy
GRUNTF1_.WAV, GRUNTF2_.WAV, GRUNTF3_.WAV" or "... GRUNTM1_.WAV do GRUNTM5_.WAV" adds those effects to the first-reaction pool.
Usage: python build_voices_ini.py [table.xlsx] [output.ini]
"""
import os
import re
import sys
import warnings

import openpyxl

warnings.simplefilter('ignore')
GAME = r'C:\Program Files\Road Rash 30th Anniversary Edition'
table = sys.argv[1] if len(sys.argv) > 1 else os.path.join(GAME, 'FFNNO_test', 'przechodnie_dzwieki.xlsx')
output = sys.argv[2] if len(sys.argv) > 2 else os.path.join(GAME, 'PedestrianVoices.ini')
# sets whose first reaction draws from both pools (people who are only ever hit at speed and never make the second reaction: the flag
# girls at the start and finish, and the skaters); a note containing the word "oba" in the table does the same for any other set
BOTH = {'FLG2', 'FLGRL', 'SKR2NW', 'SKTR2'}
# effects the game loads (the others, e.g. GRUNTM4_ / GRUNTM5_, exist as files but the game never opens them)
LOADED = ['GRUNTM1_', 'GRUNTM2_', 'GRUNTM3_', 'GRUNTF1_', 'GRUNTF2_', 'GRUNTF3_']


def names_in(text):
    """Effects in a table cell: one per line / comma, with or without ".WAV"."""
    result = []
    for token in re.split(r'[\r\n,;]+', text or ''):
        token = re.sub(r'\.wav$', '', token.strip(), flags=re.I).upper()
        if token:
            result.append(token)
    return result




def note_names(text):
    text = text or ''
    found = [n.upper() for n in re.findall(r'[A-Za-z0-9_@]+(?=\.wav)', text, flags=re.I)]
    m = re.search(r'([A-Za-z0-9_]+)\.wav\s+do\s+([A-Za-z0-9_]+)\.wav', text, flags=re.I)
    if m:
        a, b = m.group(1).upper(), m.group(2).upper()
        pa, pb = re.match(r'([A-Z_]*?)(\d+)(_?)$', a), re.match(r'([A-Z_]*?)(\d+)(_?)$', b)
        if pa and pb and pa.group(1) == pb.group(1):
            found = [pa.group(1) + str(i) + pa.group(3) for i in range(int(pa.group(2)), int(pb.group(2)) + 1)]
    return found


wb = openpyxl.load_workbook(table)
ws = wb['Postacie']
lines = ['; Made from przechodnie_dzwieki.xlsx by tools\\build_voices_ini.py - edit the Excel table, not this file.',
         '; First = pool of the first reaction (slot 2), Second = pool of the second (slot 3); one effect is drawn at random.', '']
skipped = []
count = 0
for row in ws.iter_rows(min_row=2, values_only=True):
    name = (row[0] or '').strip() if isinstance(row[0], str) else None
    if not name:
        continue
    first = names_in(row[4])
    second = names_in(row[5])
    extra = note_names(row[6])
    for e in extra:
        if e in LOADED:
            if e not in first:
                first.append(e)
        else:
            skipped.append((name, e))
    if not first and not second:
        continue
    lines.append('[%s]' % name)
    if first:
        lines.append('First=' + ','.join(first))
    if second:
        lines.append('Second=' + ','.join(second))
    if name in BOTH or re.search(r'oba', str(row[6] or ''), flags=re.I):
        lines.append('Both=1')
    lines.append('')
    count += 1
open(output, 'w', encoding='ascii', newline='\r\n').write('\r\n'.join(lines) + '\r\n')
print('sets with rules:', count, '->', output)
if skipped:
    print('not loaded by the game, left out:', ', '.join('%s %s' % s for s in sorted(set(skipped))))
