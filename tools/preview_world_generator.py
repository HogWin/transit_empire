"""
Script utilitário em Python para testar e validar o algoritmo procedural do Transit Empire
antes da compilação gráfica ou instalação de engines pesadas.
"""

import random
import math

class RoadTier:
    NONE = 0
    DIRT = 1
    GRAVEL = 2
    ASPHALT = 3
    HIGHWAY = 4
    DUAL = 5

class WorldPreview:
    def __init__(self, width=40, height=20, seed=42):
        self.width = width
        self.height = height
        self.rng = random.Random(seed)
        self.grid = [['.' for _ in range(width)] for _ in range(height)]
        self.cities = []
        self.industries = []
        self.roads = set()

    def generate(self):
        # 1. Cidades
        city_names = ["Vila Nova", "Porto Belo", "Serra Alta"]
        for name in city_names:
            cx = self.rng.randint(5, self.width - 6)
            cy = self.rng.randint(3, self.height - 4)
            self.cities.append((name, cx, cy))
            self.grid[cy][cx] = 'C'

        # 2. Indústrias
        ind_types = [("Fazenda", 'F'), ("Serraria", 'S'), ("Moinho", 'M')]
        for name, icon in ind_types:
            ix = self.rng.randint(3, self.width - 4)
            iy = self.rng.randint(2, self.height - 3)
            self.industries.append((name, ix, iy))
            self.grid[iy][ix] = icon

        # 3. Conexão de estradas de terra iniciais pelo jogo
        all_points = [(c[1], c[2]) for c in self.cities] + [(i[1], i[2]) for i in self.industries]
        for i in range(len(all_points) - 1):
            p1 = all_points[i]
            p2 = all_points[i + 1]
            self._connect_road(p1[0], p1[1], p2[0], p2[1])

    def _connect_road(self, x1, y1, x2, y2):
        cx, cy = x1, y1
        while cx != x2:
            cx += 1 if x2 > cx else -1
            if self.grid[cy][cx] == '.':
                self.grid[cy][cx] = '#'
                self.roads.add((cx, cy))
        while cy != y2:
            cy += 1 if y2 > cy else -1
            if self.grid[cy][cx] == '.':
                self.grid[cy][cx] = '#'
                self.roads.add((cx, cy))

    def simulate_city_expansion(self, city_idx=0):
        name, cx, cy = self.cities[city_idx]
        directions = [(0, 1), (1, 0), (0, -1), (-1, 0)]
        self.rng.shuffle(directions)
        for dx, dy in directions:
            nx, ny = cx + dx, cy + dy
            if 0 <= nx < self.width and 0 <= ny < self.height:
                if self.grid[ny][nx] in ('.', '#'):
                    self.grid[ny][nx] = 'c' # Expansão urbana residencial/comercial
                    return (name, nx, ny)
        return None

    def display(self):
        print("=== TRANSIT EMPIRE: PRÉVIA DO MAPA PROCEDURAL (ASCII) ===")
        print("Legenda: [C] Centro da Cidade  [c] Expansão Urbana  [F] Fazenda  [S] Serraria  [M] Moinho  [#] Estrada de Terra  [.] Terreno")
        print("-" * (self.width + 2))
        for row in self.grid:
            print("|" + "".join(row) + "|")
        print("-" * (self.width + 2))

if __name__ == "__main__":
    world = WorldPreview(seed=2026)
    world.generate()
    print("\n--- 1. Mapa Inicial Gerado pelo Jogo ---")
    world.display()

    print("\n--- 2. Cidade recebe suprimentos e se expande aleatoriamente ---")
    exp = world.simulate_city_expansion(0)
    if exp:
        print(f"-> A cidade '{exp[0]}' expandiu com sucesso para as coordenadas ({exp[1]}, {exp[2]})!")
    world.display()

