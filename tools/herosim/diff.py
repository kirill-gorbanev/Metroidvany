"""Сравнение общего прогона с эталоном: каждая изменённая строка (а значит, и число) — со старым и новым видом.
Строки с «~» (живая проверка) не сравниваются — там реальное время. Использование: python diff.py baseline.txt out/report.txt"""
import difflib
import os
import sys

base_p, new_p = sys.argv[1], sys.argv[2]


def load(p):
    if not os.path.exists(p):
        return None
    return [l.rstrip() for l in open(p, encoding='utf-8') if l.strip() and not l.startswith('~')]


new = load(new_p)
base = load(base_p)
print('----- живое (не сравнивается) -----')
for l in open(new_p, encoding='utf-8'):
    if l.startswith('~'):
        print(l.rstrip())
if base is None:
    print('эталона нет — это первый прогон (./run_all.sh accept сделает его эталоном)')
    sys.exit(0)
changed = [l for l in difflib.unified_diff(base, new, 'эталон', 'сейчас', n=0, lineterm='') if not l.startswith('@@')]
print('----- сравнение с эталоном -----')
if len(changed) <= 2:
    print('НИ ОДНО ЧИСЛО НЕ ИЗМЕНИЛОСЬ')
else:
    for l in changed[2:]:
        print(l)
    print(f'изменено строк: {sum(1 for l in changed[2:] if l.startswith("+"))}')
