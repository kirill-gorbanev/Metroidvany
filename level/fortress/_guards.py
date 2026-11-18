"""Гарнизон крепости (6) и их маршруты — в координатах замысла; _gen.py пишет guards.txt (минус CROP).

Формат строк (см. level/FORMAT.md):
  guard <id> role=<jailer|patrol|watch|archer|sleeper|wounded> L<k> i,j face=<N|E|S|W> [walk=1.5] [run=5.8] [mask=...]
  route <id> <loop|back>
  wp <id> L<k> i,j [wait=с] [look=N|E|S|W] [sit=<propId>] [do=<take|put|check>:<propId>] [every=n]
  sleep <id> bed=<propId> [wake=12] [on_wake=shout|get_up]
Стража ходит только полом, лестницами, дверями D и арками A. Вода, стремянки, люки, трубы, лазы (nav=player),
решётки, трещины, ворота G и двери с засовом — только для игрока: ими игрок рвёт погоню.
"""
GUARDS = """
# G1 тюремщик, система «еда»: кухня -> лестница лазарета -> погреб -> камеры -> пост -> обратно. Люк заперт сверху — не его путь.
guard G1 role=jailer L0 19,30 face=N walk=1.5 run=5.8 mask=walk,door,stair
route G1 loop
wp G1 L0 19,30 wait=4 look=E do=take:kitchen_bowls
wp G1 L-1 21,28
wp G1 L-1 15,28 wait=3 look=W do=put:bowls
wp G1 L-1 10,28 wait=4 look=N do=check:cell1_bed
wp G1 L-1 12,28 wait=3 look=S
wp G1 L-1 15,30 wait=8 look=S sit=jail_post

# G2 раненый в лазарете: спит, на шум 12 м кричит (зовёт G1)
guard G2 role=wounded L-1 17,29 face=E
sleep G2 bed=inf_bed1 wake=12 on_wake=shout

# G3 часовой зала и двора, система «вода»: пост лицом на запад (выход люка и западная дверь), два лепестка подряд
guard G3 role=patrol L0 14,26 face=W walk=2.0 run=5.8 mask=walk,door,stair
route G3 loop
wp G3 L0 14,26 wait=20 look=W sit=hall_post
wp G3 L0 10,26
wp G3 L0 9,24
wp G3 L0 9,18
wp G3 L0 15,16 wait=4 look=S
wp G3 L0 25,12 wait=5 look=S
wp G3 L0 22,20 wait=3 look=E
wp G3 L0 13,24
wp G3 L0 14,26 wait=20 look=W sit=hall_post
wp G3 L0 18,30
wp G3 L0 19,28
wp G3 L0 25,27 wait=2 look=N
wp G3 L0 31,25 wait=3 look=E
wp G3 L0 23,21 wait=3 look=S
wp G3 L0 14,24

# G4 спит в казарме; бросок миски со стола будит
guard G4 role=sleeper L2 11,25 face=E
sleep G4 bed=bar_bed_g4 wake=12 on_wake=get_up

# G5 дозорный: обход 4 окон дозорной
guard G5 role=watch L4 13,26 face=S walk=1.2 mask=walk,door,stair
route G5 loop
wp G5 L4 13,26 wait=8 look=S sit=watch_stool
wp G5 L4 15,29 wait=6 look=E
wp G5 L4 13,29 wait=4 look=N
wp G5 L4 12,27 wait=4 look=W

# G6 лучник, система «боезапас»: пост на ЮЗ башне -> ход по стене до пролома и обратно;
# стрелы берёт в надвратной (она же оружейная) на каждом проходе по стене
guard G6 role=archer L3 4,5 face=S walk=1.6 run=5.8 mask=walk,door,stair
route G6 back
wp G6 L3 4,5 wait=6 look=S
wp G6 L2 7,5
wp G6 L2 15,7
wp G6 L2 25,8 wait=4 look=N do=take:arrows
wp G6 L2 35,7
wp G6 L2 45,6 wait=3 look=E
wp G6 L2 43,12
wp G6 L2 43,18 wait=5 look=E
"""
