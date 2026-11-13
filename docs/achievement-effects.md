# Оформление достижений: правила и формат

Инструкция для того, кто придумывает оформление достижения — человека или языковой модели. По ней можно
сразу собрать готовый JSON и вставить его в редактор кнопкой «Вставить JSON».

## Задание для модели

Если тебе дали этот файл и описание желаемого оформления:

1. Ответь **одним** блоком кода `json` в формате из раздела «Формат JSON». Без пояснений до и после — или с ними, но JSON один.
2. Используй только перечисленные поля. Не выдумывай новых: лишние поля игнорируются, ошибочные значения сервер не сохранит.
3. Цвета — только `#rrggbb` (или `#rgb`). Никаких названий цветов, `rgb()` и прозрачности в полях конструктора.
4. Чего не нужно — не пиши вовсе (не ставь `null`, пустые строки и нули «на всякий случай»).
5. Сначала используй конструктор (`effects`). Свой CSS (`customCss`) — только для того, чего конструктор не умеет,
   и строго по правилам раздела «Свой CSS».
6. Картинки в JSON не передаются: их загружают в редакторе вручную. Если оформлению нужна картинка, включи
   соответствующий флаг (`frame`, `pattern`, `banner`, частицы) и напиши, что нарисовать и какого размера.
7. Помни про читаемость: имя должно читаться и в светлой, и в тёмной теме; фон строки — лёгкий (`opacity` 10–25).

## Как это работает

- У каждого пользователя одно **активное** достижение: его выбирает владелец в профиле. Если он ничего не выбрал,
  активным считается последнее полученное достижение с пометкой «активно по умолчанию» (`activeByDefault`).
- Оформление активного достижения видно:

| Где | Кто видит | Раздел |
|---|---|---|
| Аватар везде (таблицы, очереди, форум, профиль, меню) | все | `avatar` |
| Имя в таблице результатов, очередях, на форуме, в профиле | все | `name` |
| Строка в таблице результатов и в очереди | все | `row` |
| Страница профиля владельца | все, кто её открыл | `profile` |
| Частицы поверх всего сайта | **только владелец** | `site` |

- Если преподаватель назначил студенту цвет, имя остаётся в этом цвете — эффект имени не применяется (остальное работает).
- Любой пользователь может выключить все эффекты у себя («Язык и вид интерфейса» → «Эффекты достижений»).
- При системной настройке «уменьшить движение» анимации и частицы отключаются сами.

## Формат JSON

```json
{
  "name": "Название, до 100 символов",
  "description": "Описание, до 2000 символов",
  "activeByDefault": false,
  "effects": {
    "avatar": { "frame": false, "ringColor": "#f5b301", "ringWidth": 2, "glowColor": "#ff9d00", "animation": "none" },
    "name": { "color": "#f5b301", "color2": "#ffe08a", "bold": true, "italic": false, "glowColor": "#ff9d00", "animation": "none" },
    "row": { "color": "#f5b301", "color2": "#ff9d00", "opacity": 18, "pattern": false, "barColor": "#f5b301" },
    "profile": { "banner": false, "color": "#f5b301", "color2": "#ff9d00", "accentColor": "#f5b301", "glowColor": "#f5b301" },
    "site": { "particles": 0, "motion": "fall", "opacity": 60, "size": 28 }
  },
  "customCss": "name { text-shadow: 0 0 6px #ffd54a; }"
}
```

Все поля необязательны: при вставке в редактор отсутствующие поля не меняются, а отсутствующие разделы
`effects` сбрасываются к значениям по умолчанию.

## Поля конструктора

### `avatar` — аватар

| Поле | Значения | По умолчанию | Что делает |
|---|---|---|---|
| `frame` | `true`/`false` | `false` | Рисует картинку `frame` поверх аватара, чуть шире его краёв |
| `ringColor` | цвет | — | Цветное кольцо вокруг аватара |
| `ringWidth` | 0–4 | 2 | Толщина кольца, px |
| `glowColor` | цвет | — | Мягкое свечение вокруг аватара |
| `animation` | `none`, `pulse`, `spin` | `none` | `pulse` — пульсация яркости, `spin` — вращение рамки (нужна `frame`) |

### `name` — имя

| Поле | Значения | По умолчанию | Что делает |
|---|---|---|---|
| `color` | цвет | — | Цвет имени |
| `color2` | цвет | — | Вместе с `color` — градиент по тексту |
| `bold`, `italic` | `true`/`false` | `false` | Начертание |
| `glowColor` | цвет | — | Свечение текста |
| `animation` | `none`, `shimmer`, `rainbow` | `none` | `shimmer` — перелив градиента (нужны оба цвета), `rainbow` — смена оттенка |

### `row` — строка таблицы результатов и очереди

| Поле | Значения | По умолчанию | Что делает |
|---|---|---|---|
| `color` | цвет | — | Цвет фона сверху |
| `color2` | цвет | — | Цвет фона снизу (иначе как `color`) |
| `opacity` | 0–60 | 18 | Насыщенность фона, %. Строка остаётся читаемой примерно до 25 |
| `pattern` | `true`/`false` | `false` | Повторяет картинку `pattern` вдоль строки |
| `barColor` | цвет | — | Цветная полоса 3 px слева |

### `profile` — страница профиля

| Поле | Значения | По умолчанию | Что делает |
|---|---|---|---|
| `banner` | `true`/`false` | `false` | Картинка `banner` за шапкой профиля |
| `color`, `color2` | цвет | — | Лёгкий градиентный фон страницы |
| `accentColor` | цвет | — | Цвет кнопок и ссылок на странице профиля |
| `glowColor` | цвет | — | Обводка и свечение шапки профиля |

### `site` — декор сайта (видит только владелец)

| Поле | Значения | По умолчанию | Что делает |
|---|---|---|---|
| `particles` | 0–12 | 0 | Сколько копий картинки `decor` движется поверх сайта. 0 — выключено |
| `motion` | `fall`, `float` | `fall` | Падают сверху вниз или парят на месте |
| `opacity` | 5–100 | 60 | Непрозрачность частиц, % |
| `size` | 8–96 | 28 | Размер частицы, px |

Частицы не перехватывают клики. Делайте их некрупными и полупрозрачными: владелец видит их на каждой странице.

## Картинки

Загружаются в редакторе после первого сохранения достижения. Подходят PNG, WebP, JPEG, GIF, MP4, WebM, MOV
до 20 МБ; анимация — до 10 секунд. Прозрачность PNG/WebP/GIF сохраняется.

| Слот | Форма и размер | Что рисовать | Включается |
|---|---|---|---|
| `badge` | квадрат 512×512 | Значок достижения: в каталоге, профиле и рядом с именем | всегда |
| `frame` | квадрат 512×512 | Рамка с **прозрачной серединой**; ложится поверх аватара и выходит за край примерно на 18% | `avatar.frame` |
| `pattern` | квадрат 256×256 | Бесшовная плитка, повторяется вдоль строки; лучше светлая и полупрозрачная | `row.pattern` |
| `banner` | 1600×400 (4:1) | Широкая картинка за шапкой профиля; низ плавно уходит в фон | `profile.banner` |
| `decor` | квадрат 256×256 | Одна частица: снежинка, звезда, лист, искра | `site.particles` > 0 |

В своём CSS картинка вызывается как `image(frame)`, `image(pattern)`, `image(banner)`, `image(decor)`, `image(badge)`.

## Свой CSS

Дописывается после конструктора и может его переопределить. Это ограниченный диалект: сервер проверяет каждое
правило и **отказывает целиком**, если что-то не по правилам (прежнее оформление при этом сохраняется).
Ошибки показываются в редакторе с номером строки.

### Поверхности

Правило начинается с поверхности — это и есть селектор. Ничего другого (`body`, классы, `>`, `*`) писать нельзя.

| Селектор | Что это | Разрешённые свойства |
|---|---|---|
| `avatar` | картинка аватара | «рамочные» |
| `avatar::before`, `avatar::after` | слои поверх аватара, не обрезаются | «слои» |
| `name` | имя владельца | «покраска» |
| `row` | строка таблицы результатов и очереди | «строка» |
| `profile`, `profile::before`, `profile::after` | вся страница профиля | «рамочные» / «слои» |
| `hero`, `hero::before`, `hero::after` | шапка профиля (аватар, имя, кнопки) | «рамочные» / «слои» |
| `site` | слой поверх сайта у владельца | `opacity`, `filter`, `mix-blend-mode`, анимации |
| `site i`, `site i:nth-child(…)` | 12 пустых частиц в этом слое (по умолчанию скрыты, `display: block` показывает) | «слои» |

К `avatar`, `name`, `row`, `profile`, `hero` можно добавить `:hover`. Несколько селекторов — через запятую.

### Группы свойств

- **Анимации** (есть во всех группах): `animation`, `animation-*`, `transition`, `transition-*`.
- **Строка**: `color`, `background-image`, `background-size`, `background-position`, `background-repeat`,
  `background-blend-mode`, `box-shadow`, `text-shadow`, `opacity`, `font-weight`, `font-style`.
  Размеры строки менять нельзя: таблица держит фиксированную высоту строки. Сплошной фон — через
  `background-image: linear-gradient(…)`, а не `background`.
- **Покраска** (`name`): `color`, `background`, `background-color`, `background-image`, `background-size`,
  `background-position`, `background-repeat`, `background-clip`, `-webkit-background-clip`, `-webkit-text-fill-color`,
  `background-origin`, `background-blend-mode`, `box-shadow`, `text-shadow`, `opacity`, `filter`, `border-color`,
  `border-radius`, `outline`, `outline-*`, `font-weight`, `font-style`, `font-variant`, `text-decoration`, `text-decoration-*`,
  `text-underline-offset`, `text-transform`, `letter-spacing`, `mix-blend-mode`.
- **Рамочные** (`avatar`, `profile`, `hero`): покраска + `transform`, `transform-origin`, `rotate`, `scale`, `translate`,
  `clip-path`, `border`, `border-width`, `border-style`, `padding`, `padding-*`.
- **Слои** (`::before`, `::after`, `site i`): рамочные + `content`, `position`, `inset`, `top`, `right`, `bottom`, `left`,
  `z-index`, `display`, `width`, `height`, `min-*`/`max-*`, `aspect-ratio`, `margin`, `margin-*`, `border-top` и т. п.,
  `border-image`, `border-image-*`, `mask`, `mask-*`, `-webkit-mask`, `-webkit-mask-*`, `font-size`, `line-height`,
  `text-align`, `white-space`, `vertical-align`, `overflow`, `backdrop-filter`, `will-change`.

Ограничения значений:
- `position`: только `absolute`, `relative`, `static` (без `fixed`);
- `z-index`: от −1 до 9;
- `display`: `block`, `inline-block`, `inline`, `flex`, `inline-flex`, `grid`, `none`;
- `content`: короткий текст в кавычках (до 60 символов), `none` или `normal` — например `content: "★";`.

### Значения

- Разрешены числа, единицы, `#цвета`, слова и функции: `rgb`, `rgba`, `hsl`, `hsla`, `hwb`, `oklch`, `oklab`,
  `color-mix`, все `*-gradient`, `calc`, `min`, `max`, `clamp`, `var`, функции `transform` (`translate*`, `scale*`,
  `rotate*`, `skew*`, `perspective`, `matrix`), функции `filter` (`blur`, `brightness`, `contrast`, `drop-shadow`,
  `grayscale`, `hue-rotate`, `invert`, `saturate`, `sepia`, `opacity`), `cubic-bezier`, `steps`, `circle`, `ellipse`,
  `inset`, `polygon`, а также `image(слот)`.
- Нельзя: `url()`, `!important`, кавычки где-либо кроме `content`, символы `\` и `<`, вложенные правила, `@import`,
  `@font-face`, другие `@`-правила. Лимит — 8000 символов.

### `@`-правила

- `@keyframes имя { from {…} 50% {…} to {…} }` — свои анимации. В шагах можно менять только `opacity`, `color`,
  `background-position`, `background-size`, `box-shadow`, `text-shadow`.
  В `animation` имя пишется **первым**, дальше — длительность и параметры: `animation: glow 2s ease-in-out infinite;`.
  Использовать можно только анимации, объявленные в этом же CSS.
- `@media (prefers-reduced-motion: reduce) {…}`, `@media (max-width: 600px) {…}`, `@media (min-width: …px) {…}`.
- `@light {…}`, `@dark {…}` — правила только для светлой или только для тёмной темы.

## Готовые примеры

Все примеры проходят проверку сервера — их можно вставлять как есть.

### Золото

```json
{
  "name": "Золотой код",
  "description": "За безупречные решения без единой доработки.",
  "effects": {
    "avatar": { "ringColor": "#f5b301", "ringWidth": 2, "glowColor": "#ff9d00", "animation": "pulse" },
    "name": { "color": "#e8a600", "color2": "#ffd54a", "bold": true, "animation": "shimmer" },
    "row": { "color": "#f5b301", "opacity": 16, "barColor": "#f5b301" },
    "profile": { "color": "#f5b301", "accentColor": "#e8a600", "glowColor": "#f5b301" }
  },
  "customCss": "hero::after { content: \"★\"; position: absolute; right: 18px; top: 12px; font-size: 26px; color: #f5b301; }"
}
```

### Неон

```json
{
  "name": "Ночной кодер",
  "description": "Сдал лабораторную после полуночи.",
  "effects": {
    "avatar": { "ringColor": "#22d3ee", "ringWidth": 2, "glowColor": "#22d3ee" },
    "name": { "color": "#22d3ee", "bold": true },
    "row": { "color": "#0ea5e9", "color2": "#a855f7", "opacity": 20, "barColor": "#22d3ee" },
    "profile": { "color": "#0ea5e9", "color2": "#a855f7", "accentColor": "#22d3ee", "glowColor": "#a855f7" }
  },
  "customCss": "@keyframes flicker { 0%, 100% { opacity: 1; } 50% { opacity: .55; } }\nname { text-shadow: 0 0 6px #22d3ee, 0 0 14px #22d3ee; animation: flicker 2.4s ease-in-out infinite; }\n@light { name { color: #0e7490; text-shadow: none; } }\n@media (prefers-reduced-motion: reduce) { name { animation: none; } }"
}
```

### Зима (с картинками)

Картинки: `frame` — венок из еловых веток с прозрачной серединой; `pattern` — бледные снежинки на прозрачном фоне;
`banner` — заснеженный лес 4:1; `decor` — одна белая снежинка.

```json
{
  "name": "Зимняя сессия",
  "description": "Закрыл все работы до Нового года.",
  "activeByDefault": false,
  "effects": {
    "avatar": { "frame": true, "glowColor": "#bfe3ff" },
    "name": { "color": "#7cc4ff", "color2": "#e0f2ff", "bold": true },
    "row": { "color": "#7cc4ff", "opacity": 12, "pattern": true },
    "profile": { "banner": true, "accentColor": "#3b9ae1" },
    "site": { "particles": 10, "motion": "fall", "opacity": 70, "size": 22 }
  }
}
```

## Частые ошибки

| Сообщение | Что исправить |
|---|---|
| `'body' is not a surface…` | Правило должно начинаться с `avatar`, `name`, `row`, `profile`, `hero` или `site` |
| `'height' cannot be used in row.` | У строки нельзя менять размеры и отступы; используйте слои или профиль |
| `url() is not allowed…` | Загрузите картинку в нужный слот и пишите `image(слот)` |
| `The value of '…' has characters that are not allowed…` | Убрать кавычки (они есть только в `content`), `:` и прочие символы из значения |
| `Declare the animation with @keyframes in this achievement.` | Объявить `@keyframes` с этим именем в этом же CSS и писать имя первым |
| `'transform' cannot be used in an animation step.` | В `@keyframes` — только `opacity`, `color`, `background-position`, `background-size`, `box-shadow`, `text-shadow` |
| `!important is not allowed.` | Убрать `!important` |
| `name.color: use a hex colour like #7c3aed.` | Поля конструктора принимают только `#rrggbb` |
