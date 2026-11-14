"""Build the offline wardrobe exception list from pinned public game and acquisition data.

Input directory: English Cabinet, Quest, JournalGenre, Achievement, AchievementCategory,
SpecialShop CSV files from xivapi/ffxiv-datamining at 52a72b38aa2f967cab36867f34cd9bee9debb88b,
and libs/data/src/lib/extracts/extracts.json from ffxiv-teamcraft/ffxiv-teamcraft
at 2eaa77b321579ee155372ec4bb517feda60a2b0d. Unknown sources require ownership.
"""
import csv
import json
import sys
from pathlib import Path


def generate(directory):
    def rows(name):
        with (directory / (name + '.csv')).open(encoding='utf-8-sig', newline='') as file:
            yield from csv.DictReader(file)

    seasonal = {int(r['Item']) for r in rows('Cabinet') if r['Category'] in {'2', '5', '6', '7', '8'}}
    genres = {r['#'] for r in rows('JournalGenre') if r['JournalCategory'] == '97'}
    quests = {int(r['#']) for r in rows('Quest') if int(r['Festival']) or r['JournalGenre'] in genres}
    categories = {r['#'] for r in rows('AchievementCategory') if r['Name'] == 'Seasonal Events'}
    achievements = {int(r['#']) for r in rows('Achievement') if r['AchievementCategory'] in categories}
    shops = {int(r['#']) for r in rows('SpecialShop') if int(r['RequiredFestival']) or int(r['Quest']) in quests}
    extracts = json.loads((directory / 'extracts.json').read_text(encoding='utf-8'))
    candidates = set(seasonal)
    normal = set()
    exchanges = {}
    for row in extracts.values():
        item = row['id']
        for source in row['sources']:
            kind, data = source['type'], source['data']
            if kind == 21:  # Mog Station
                candidates.add(item)
            elif kind in (18, 19):  # quest / achievement rewards
                events = quests if kind == 18 else achievements
                if any(i in events for i in data):
                    candidates.add(item)
                if any(i not in events for i in data):
                    normal.add(item)
            elif kind == 3:
                for vendor in data:
                    # Recompense Officers sell previously earned seasonal rewards.
                    if vendor.get('festival') or vendor['npcId'] in {1017613, 1017614, 1017615, 1025913}:
                        candidates.add(item)
                    else:
                        normal.add(item)
            elif kind == 2:
                for shop in data:
                    if shop.get('id') in shops or shop.get('shopName', {}).get('en', '').startswith('Seasonal Event Prizes'):
                        candidates.add(item)
                    elif shop.get('shopName', {}).get('en', '').startswith('Gender-specific Gear Exchange'):
                        for trade in shop['trades']:
                            exchanges.setdefault(item, []).append({c['id'] for c in trade['currencies']})
                    else:
                        normal.add(item)
            elif kind not in (-1, 0, 11, 12, 20):
                normal.add(item)

    allowed = candidates - normal
    while True:
        # A conversion is a normal acquisition route if all inputs are obtainable normally.
        updated = {i for i in allowed if all(inputs & allowed for inputs in exchanges.get(i, []))}
        if updated == allowed:
            return sorted(allowed - {0})
        allowed = updated


if __name__ == '__main__':
    result = generate(Path(sys.argv[1]))
    output = Path(__file__).resolve().parents[1] / 'Glamourer/Unlocks/WardrobeExceptions.json'
    output.write_text(json.dumps(result) + '\n', encoding='utf-8')
    print(f'{len(result)} exception IDs')

