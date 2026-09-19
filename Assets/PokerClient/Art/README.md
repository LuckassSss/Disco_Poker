# Куда класть ассеты

- `Cards/` — 52 спрайта карт + рубашка. Имена: `As.png`, `Kh.png`, `Td.png`, `2c.png` ... `back.png` (rank: 2-9,T,J,Q,K,A; suit: s,h,d,c).
- `Chips/` — фишки по номиналам: `chip_1.png`, `chip_5.png`, `chip_25.png`, `chip_100.png`, ...
- `Table/` — фон стола, дилер-баттон (`dealer.png`).
- `UI/` — пришедший арт-пак (HUD, карта локаций `Locations/Location01..04`, `Loading/`, `Map/`, `Tutorial/`,
  кадры диско-шара `Animations/Balls/`). Файлы не переименовывать; ключи для кода — `id` из `UI/Documentation/ui-assets.json`,
  список PNG с запечённым текстом — `UI/Documentation/README.md`. Кнопок Fold/Call/Raise и панелей стола пока нет.
- `Fonts/` — .ttf/.otf для TMP.
- `Audio/` — звуки: раздача, фишки, таймер.

Формат: PNG с альфой, спрайты одного типа — одного размера.
