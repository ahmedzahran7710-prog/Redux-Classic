# 📘 AGENT_HANDOFF.md — المرجع الشامل وذاكرة المشروع (Comet 5187)

> **تعليمات لأي AI Agent مستقبلي يقرأ هذا الملف:**
> هذا الملف هو **الذاكرة المركزية الموثقة (Single Source of Truth)** لمشروع تطوير سيرفر **Conquer Online — Patch 5187 (Classic + Ninja)** المبني من الصفر على سورس **Comet** الأصلي لـ *Spirited*.
> 1. اقرأ هذا الملف بالكامل قبل إجراء أي تعديل برمجي.
> 2. التزم بجميع القواعد المعمارية وقواعد حماية مساحة الـ Workspace المذكورة بالأسفل.
> 3. **حدّث قسم سجل التقدم (Progress Checklist & Changelog) في هذا الملف بعد الانتهاء من كل مرحلة أو مهمة برمجية.**

---

## 1. هوية المشروع والهدف الأساسي
* **اسم اللعبة الرسمي (Game Brand):** **`Mystic-Realms`** (موحّد بالكامل عبر `src/Comet.Shared/GameBranding.cs`، الداتابيز، اللانشر، رسائل السيرفر، وجميع محادثات الـ NPCs).
* **اسم المشروع التقني:** Mystic-Realms — Comet 5187 Server (Built Cleanly from Scratch).
* **الإصدار المستهدف:** **Patch 5187** (آخر إصدار 2D كلاسيك متوازن يقف عند شخصية الـ **Ninja** بدون Monk/Pirate وبدون Chi/JiangHu/3D).
* **الأساس البرمجي:** سورس **Comet** الخام (`net6.0` / C#) من تطوير *Gareth Jensen (Spirited)*.
* **أسلوب العمل الحالي:** المالك يعمل حالياً من الهاتف، لذلك نقوم ببناء وتجهيز ومراجعة كافة الأنظمة برمجياً بهدوء ونظافة تامة هنا على المستودع قبل مرحلة التجربة والتشغيل الفعلي على الكمبيوتر/VPS.

---

## 2. هيكل المشروع والملفات (Repository Structure)

```text
/
├── AGENT_HANDOFF.md              # هذا الملف (المرجع الشامل وذاكرة المشروع لأي إيجنت)
├── README.md                     # دليل المشروع والتشغيل السريع
├── Comet.sln                     # ملف الـ Solution الرئيسي للسيرفر
├── sql/
│   ├── comet.account.sql         # قاعدة بيانات الحسابات والـ Realms + Trigger التشفير
│   ├── comet.game.sql            # قاعدة بيانات الجيم (تبدأ بجدول character وتُوسّع مع كل مرحلة)
│   └── upgrades/                 # سكريبتات الترقية المرجعية
├── src/
│   ├── Comet.Account/            # سيرفر تسجيل الدخول (Port 9960)
│   ├── Comet.Game/               # سيرفر العالم واللعب (Game Port 5816 / RPC Port 5817)
│   ├── Comet.Network/            # طبقة الشبكة (Async Sockets, Channels, RPC, Ciphers)
│   ├── Comet.Core/               # دوال مساعدة ورياضيات أساسية
│   └── Comet.Shared/             # نماذج البيانات المشتركة بين Account و Game عبر RPC
├── tests/                        # اختبارات الوحدات (Account, Core, Network)
└── tools/
    ├── DragonLauncher/           # كود اللودر الرسمي (C++ Win32) لتوجيه الـ IP وحل مشكلة الفلاش
    └── Comet.AutoPatcher/        # أوتوباتشر ولانشر خفيف بـ C# لتحديث الكلينت وتشغيل اللودر
```

---

## 3. المواصفات التقنية الدقيقة لباتش 5187

### أ) البورتات والشبكة (Network & Ports)
| الخدمة | البورت الافتراضي | ملف الإعدادات | الوظيفة |
|---|---|---|---|
| **Comet.Account** | `9960` | `src/Comet.Account/Comet.Account.config` | استقبال اتصال الكلينت الأول والتحقق من الحساب |
| **Comet.Game (Client)** | `5816` | `src/Comet.Game/Comet.Game.config` | اتصال اللاعب بعالم اللعبة |
| **Comet.Game (RPC)** | `5817` | `src/Comet.Game/Comet.Game.config` | قناة اتصال داخلية بين `Account` و `Game` لنقل التوكن |

> **ملاحظة مهمة عند التشغيل الفعلي:** في جدول `realm` داخل قاعدة بيانات `comet.account`، الحقل `GameIPAddress` يجب أن يكون IP الشبكة المحلي (`192.168.x.x`) أو الـ Public IP للـ VPS (كلينت كونكر العادي يرفض `127.0.0.1` في `MsgConnectEx` إلا إذا تم تفعيل `game_server.enable_redirect: true` في `Dragon.Module.Connect.json`).

### ب) التشفير وتسلسل الدخول (Encryption & Login Flow - Patch 5187)
1. يتصل الكلينت بـ `Comet.Account` (تشفير الباكتات `TQCipher`)، فيرسل السيرفر `MsgEncryptCode (1059)` وبداخله `Seed` عشوائي.
2. يرد الكلينت بـ `MsgAccount (1086)` وبداخله الاسم والباسورد مشفراً بـ **RC5 + ScanCodeCipher**.
3. يتحقق `Comet.Account` من الباسورد في جدول `account` (`SHA1(Password + Salt)`), ثم يرسل طلب RPC إلى `Comet.Game` لحجز Token الدخول، ويرد على الكلينت بـ `MsgConnectEx (1055)`.
4. يتصل الكلينت بـ `Comet.Game`، فيبدأ تبادل مفاتيح **Diffie-Hellman (`MsgHandshake`)** لتوليد مفتاح **Blowfish CFB64** مع ختم `TQServer` / `TQClient` (8 بايت في نهاية كل باكت).
5. يرسل الكلينت `MsgConnect (1052)`، فإما يرد السيرفر بـ `"NEW_ROLE"` لفتح شاشة إنشاء الشخصية (`MsgRegister 1001`)، أو يرد بـ `"ANSWER_OK"` متبوعاً بـ `MsgUserInfo (1006)` وسلسلة `MsgAction (10010)`.

### ج) تحذير هام جداً في أرقام باكتات 5187!
في إصدار **5187** قامت شركة TQ بتغيير أرقام 4 باكتات أساسية إلى فئة `10000+` (موجودة بالفعل في `src/Comet.Network/Packets/PacketTypes.cs`):
* `MsgWalk` = **`10005`** (وليس 1005)
* `MsgAction` = **`10010`** (وليس 1010)
* `MsgPlayer` = **`10014`** (وليس 1014)
* `MsgUserAttrib` = **`10017`** (وليس 1017)

### د) الشخصيات المعتمدة في السورس (6 شخصيات حتى النينجا فقط)
1. **Trojan:** `10 -> 15` (Trojan Master)
2. **Warrior:** `20 -> 25` (Warrior Master)
3. **Archer:** `40 -> 45` (Archer Master)
4. **Ninja:** `50 -> 55` (Ninja Master)
5. **Water Taoist:** `100 -> 132 -> 135` (Water Saint)
6. **Fire Taoist:** `100 -> 142 -> 145` (Fire Saint)

---

## 4. القواعد المعمارية الصارمة (Mandatory Architecture Rules)

لتجنب كافة الأخطاء الشهيرة في سورسات كونكر الأخرى (مثل World Conquer v1 أو Redux):

1. **منع الـ Deadlock والـ Race Conditions نهائياً (Per-Map Channel Design):**
   * `PacketProcessor<Client>` يعالج الباكتات على عدة Partitions بالتوازي.
   * عند بناء نظام الخرائط والقتال في المرحلة 1 وما بعدها، يُمنع تعديل حالة الخريطة أو الوحوش أو صحة اللاعبين مباشرة من خيوط متفرقة بدون تزامن.
   * **الحل المعتمد:** إنشاء `WorldProcessor` يحتوي على `Channel` مستقل لكل خريطة (أو مجموعة خرائط متجاورة غير متداخلة + فصل خرائط الـ PvP عن خرائط الوحوش) بحيث تُنفَّذ أوامر الحركة، الهجوم، الدروب، والتجارة داخل الخريطة الواحدة بالتسلسل (`Single-Reader Channel`) بدون الحاجة إلى `lock` متداخلة تسبب تجميد السيرفر.
2. **استخدام مؤقتات 64-بت دائماً (`Environment.TickCount64`):**
   * يُمنع استخدام `Environment.TickCount` (32-bit) لأنه ينقلب لرقم سالب بعد 24.8 يوم من عمل نظام التشغيل ويوقف التايمرات؛ يُستخدم دائماً `Environment.TickCount64`.
3. **نظام سكريبتات C# نظيف للـ NPCs والأيتمات والإيفنتات:**
   * بدلاً من تعقيد جداول `cq_action` الطويلة في الداتابيز، نبني نظام `INpcScript` / `IItemScript` نظيف بالـ Attributes (مثل `[NpcScript(10003)]`) مع إمكانية دعم الحوارات والـ Tasks بمرونة وسهولة قراءة 100%.
4. **حماية مساحة الـ Workspace ومستودع Git (قاعدة قصوى):**
   * الحد التراكمي للـ Snapshots في البيئة السحابية ~128 MB وحد الملف الواحد على GitHub هو 100 MB.
   * **ممنوع منعاً باتاً** وضع الكلينت أو ملفات مضغوطة كبيرة (`*.zip`, `*.rar`, `*.7z`) أو ملفات `bin/` و `obj/` أو ملفات `.dmap` الضخمة داخل Git.
   * المستودع يحتوي فقط على الكود المصدري النظيف، ملفات SQL، ملفات الإعدادات، وأدوات اللودر والأوتوباتشر الخفيفة.
   * أما ملفات الداتا الخاصة بالكلينت (`ini/` و `map/*.dmap`) فتُقرأ وقت التشغيل من مسار الكلينت أو مجلد خارجي مُستثنى في `.gitignore`.
5. **قواعد المتابعة والتوثيق التعليمي الإلزامية مع المالك (Workflow Rules):**
   * **المتابعة الحية أثناء الشغل:** أثناء تنفيذ أي مرحلة أو مهمة متعددة الخطوات، يتم إرسال رسائل تقدم مختصرة وواضحة في الشات بين الخطوات ("خلصت كذا وجاري تظبيط كذا...") حتى يتابع المالك سير العمل لحظة بلحظة من الهاتف.
   * **مجلد `قسم معلومات التعديلات/` على GitHub:** بعد كل إضافة أو تعديل برمجي، يجب إنشاء ملف `.md` مخصص داخل مجلد `قسم معلومات التعديلات/` ورفعه على GitHub يشرح بأسلوب منظم ومبسط جداً (يناسب مبتدئ تماماً وبخبرة مبرمج محترف):
     1. ما الذي تم إضافته أو تعديله ولماذا.
     2. شرح الكود والملفات المسؤولة عنه ببساطة ووضوح.
     3. كيف يعدّل المالك عليه يدوياً مستقبلاً (الأرقام، المعادلات، الأماكن، وطريقة التغيير خطوة بخطوة).
   * **ملفات متطلبات الكلينت أو الأدوات الخارجية (باسم عربي + إنجليزي):** إذا احتاجت أي إضافة لتعديل أو نسخ ملفات من الكلينت أو إعداد خارج السورس، يتم تجهيز ملف خطوات مستقل داخل `قسم معلومات التعديلات/` باسم مزدوج **عربي وإنجليزي** ورفعه على GitHub ليرجع له المالك فور فتح الكمبيوتر.
   * **الفحص الصارم الصفري للأخطاء:** التحقق الدائم من الربط الكامل بين كل الملفات والباكتات والداتابيز وشجرة الـ AST لضمان 0 أخطاء.

---

## 5. متطلبات الكلينت واللودر والأوتوباتش (عند التجربة على الكمبيوتر)

1. **الكلينت المطلوب:** كلينت **Conquer Online v5187 الخام (Unmodified Clean Client)**.
2. **هل يحتاج باتش حالياً؟** لا يحتاج أي باتش مخصص في البداية (فقط التأكد من وجود ملف `Flash.ocx` القديم الخاص بواجهة إنشاء الشخصية أو استخدام موديول `Dragon.Module.Flash` المرفق، وحذف مجلد حماية TQ القديم إن وُجد). نستخدم الباتشات المخصصة فقط لاحقاً لو أضفنا جيرمنتات/خرائط/تعديلات واجهة خاصة بنا.
3. **اللودر (متوفر خياران):**
   * **الخيار الأول (مرفوع معنا في المستودع):** `tools/DragonLauncher` — يُبنى بـ Visual Studio (وضعية `Win32 / x86`) ويُعدّل ملف `Dragon.Module.Connect.json` بالـ IP والبورت `9960`، ويشمل حل الفلاش تلقائياً.
   * **الخيار الثاني (جاهز بدون بناء C++):** استخدام **ConquerLoader** المفتوح المصدر (`OpenConquerOrg/ConquerLoader`) مع ضبط البورت `9960` وإصدار `5187`.
4. **الأوتوباتشر:** مرفوع ومجهز في `tools/Comet.AutoPatcher` بـ C# وجاهز للربط بأي استضافة ويب لرفع تحديثات `.zip` تلقائياً للاعبين وقت الافتتاح.

---

## 6. خارطة طريق التطوير من الصفر (Development Roadmap)

- [x] **المرحلة 0 (التأسيس والرفع):**
  - [x] تحميل سورس **Comet 5187** الخام النظيف ورفعه للمستودع.
  - [x] إضافة كود **Dragon Launcher** في `tools/DragonLauncher`.
  - [x] بناء **Comet.AutoPatcher** خفيف بـ C# في `tools/Comet.AutoPatcher`.
  - [x] تأمين `.gitignore` لحماية مساحة الـ Workspace على المدى الطويل.
  - [x] إنشاء وتوثيق `AGENT_HANDOFF.md` و `README.md`.
- [x] **المرحلة 1 (نواة العالم، الخرائط، الحركة، الرؤية، والشات) — مكتملة 100%:**
  - [x] إصلاح كافة أخطاء سورس Comet 5187 الخام (معكوسية `StrictAuthentication`، تعارض مفاتيح `Kernel.Clients` بين `AccountID` و `CharacterID`، تسريب أوزان الـ Partitions في `Account/Server.cs`، فحص الحظر والصيانة في `MsgAccount`، نقاط التاويست المبدئية في `MsgRegister`، ونوع `VIPLevel` في EF Core).
  - [x] بناء `TimeOut` / `TimeOutMS` بـ `Environment.TickCount64` + حسابات `ScreenCalculations` (Chebyshev, 8-Direction, Bresenham Raycast) + نظام `Log` الملوّن في `Comet.Shared`.
  - [x] بناء `WorldProcessor` (Per-Map Single-Reader Channels لمنع الـ Deadlock: `0`=NoMap, `1`=PvP, `2..N`=Normal) + `WorldTimerService` (نبضة عالم 500ms بدون تراكم).
  - [x] بناء نظام الخرائط الرسمي الكامل: `Tile`, `GameBlock` (18x18), `GameMapData` (`ini/GameMap.dat` + `.DMap` غير مضغوط + `.scn` مع دعم المسارات غير الحساسة لحالة الأحرف وشبكة احتياطية آمنة عند غياب الملفات), `Weather`, `GameMap`, `MapManager`.
  - [x] تزويد `sql/comet.game.sql` ببيانات كونكر الرسمية الأصيلة لباتش 5187 (273 خريطة `cq_map` بأسماء إنجليزية نظيفة، 347 ممر `cq_passway`، 322 بوابة `cq_portal`، 26 منطقة `cq_region`، 139 مستوى خبرة `cq_levexp`، و600 صف توزيع نقاط تلقائي `cq_point_allot` للـ 5 فئات الأساسية حتى النينجا) + نماذج ومستودعات EF Core مطابقة 100%.
  - [x] بناء `Role`, `Character`, `RoleManager`, `ExperienceManager` ونظام الرؤية ثنائي الاتجاه `Screen` (`VIEW_SIZE = 18`) + باكت `MsgPlayer (10014)` و `MsgUserAttrib (10017)` و `MsgMapInfo (1110)` و `MsgWeather (1016)` و `MsgData (1033)` و `MsgPing (1037)` و `MsgName (1015)` و `MsgAllot (1024)`.
  - [x] تفعيل المشي خطوة وخطوتين (`RunDir0..7`) والنط المفحوص بالارتفاع (`<= 210`) والانتقال عبر البوابات (`MsgWalk 10005`, `MsgAction 10010`: LoginSpawn, Jump, Direction, Emote, Portal, PkMode, Observation, Away, ChangeFace).
  - [x] تفعيل الشات الكامل (`MsgTalk 1004`) ونظام `CommandManager` لأوامر اللاعبين والـ GM (`/help`, `/chgmap`, `/tc`..`/gw`, `/move`, `/goto`, `/bring`, `/level`, `/exp`, `/pro`, `/reborn`, `/money`, `/cps`, `/str`..`/points`, `/heal`, `/pk`, `/vip`, `/hair`, `/avatar`, `/body`, `/weather`, `/bc`, `/kick`, `/where`, `/online`, `/dc`).
- [x] **المرحلة 2 (نظام الأيتمات والشنطة والمعدات والشوبات) — مكتملة 100%:**
  - [x] جداول `cq_item`, `cq_itemtype`, `cq_itemaddition`, `cq_goods` + حقل `WarehouseSilver` في SQL + قارئ مزدوج يفك تشفير `ini/itemtype.dat` (`0x2537`) ويقرأ `ItemAdd.ini` و `Shop.dat` تلقائياً مع أمر `/syncitemsdb`.
  - [x] باكتات الأيتمات الكاملة لباتش 5187 (`MsgItemInfo 1008`, `MsgItem 1009`, `MsgPackage 1102`, `MsgGemEmbed 1027`, `MsgDataArray 2036`, `MsgEquipLock 2048`, `MsgMapItem 1101`).
  - [x] اللبس والخلع لقواعد 5187 (شاملاً 2 كاتانا للنينجا، القوس والأسهم، الدرع، التليزمانات، الحصان، والجيرمنت)، الشنطة (40 خانة + دمج وتقسيم)، البنوك (إيداع وسحب فضة وأيتمات + بنوك الـ VIP)، الشراء والبيع، التصليح، ورمي والتقاط الأيتمات والفضة (`MapItem`).
  - [x] التطوير الرسمي (`+1` إلى `+12` بالحجارة والمعدات وخلط ألوان الحصان RGB)، رفع الجودة بالدراجون بول واللفل بالميتور مع فرصة فتح سوكت، تركيب وخلع الجواهر (شاملاً Thunder/Glory للمروحة والبرج)، الـ Enchant بالـ Gems، شحن سوكتات التليزمان، قفل الأيتمات بمهلة 5 أيام، وحساب الـ Battle Power.
  - [x] نظام سكريبتات الأيتمات بـ C# (`ItemScriptEngine` / `[ItemScript]` للجرعات، لفافات المدن، كرات الخبرة، لفافات الميتور والدراجون بول، صبغات الشعر، وشنط الكبس).
- [x] **المرحلة 3 (الوحوش، الذكاء الاصطناعي، الدروب، القتال الأساسي، اللفلفة والريبورن) — مكتملة 100%:**
  - [x] جداول `cq_monstertype` (579 وحش وحارس رسمي بـ 46 عموداً لباتش 5187)، `cq_generator` (1,377 منطقة إسباون رسمية)، و `cq_rebirth` (72 مسار ريبورن رسمي للكلاسات الـ 6 حتى النينجا) في `sql/comet.game.sql` + نماذج ومستودعات EF Core مطابقة 100%.
  - [x] كلاس `Monster` + إعادة تدوير الـ UIDs (`400001..499990`) + ذكاء اصطناعي ذكي ينام تلقائياً عند خلو مربعات الرؤية (`3x3 GameBlocks`) ويشمل حراس المدن (`Guard` / `Patrol`) والوحوش الهجومية والمسالمة + مزامنة لحظية مع `Screen` (`MsgPlayer` / `MsgWalk` / `RemoveEntity`).
  - [x] مدير المناطق `Generator` و `MonsterManager` + محرك الدروب الرسمي القابل للتخصيص `DropManager` (فضة + انفجار الفضة المتناثر، جرعات، معدات وأسلحة مطابقة للفل الوحش من Normal حتى Super، ميتور، دراجون بول مع برودكاست، وخيار كبس جاهز).
  - [x] باكت القتال والتفاعل لباتش 5187 (`MsgInteract 1022`: الضرب الجسدي المتواصل، الرمي بالقوس وخصم الأسهم، فك تشفير `MagicAttack` لباتش 5187 استعداداً للمرحلة 4، الموت كشبح وإعادة الإحياء `CharacterRevive 94`، حساب بونص الجواهر والتليزمان والـ Bless والـ Battle Power).
  - [x] نظام الخبرة من الوحوش حسب فرق اللفل، والـ 1st Reborn والـ 2nd Reborn ومنحنى النقاط الإضافية والـ Enlightenment في `RebirthManager` + أوامر الـ GM (`/spawnmob`, `/rebirth`, `/rates`, `/revive`).
- [x] **المرحلة 4 (نظام القتال، الـ Battle Power، خبرة الأسلحة، ومهارات الـ 6 شخصيات حتى النينجا) — مكتملة 100%:**
  - [x] جداول `cq_magictype` (803 صف رسمي لباتش 5187 بدون Monk/Pirate)، `cq_magic`، و `cq_weapon_skill` في `sql/comet.game.sql` + قارئ `MagicManager` المزدوج (`ini/magictype.dat` المشفر بـ `0x2537` + الداتابيز).
  - [x] باكتات المهارات الكاملة لباتش 5187 (`MsgMagicInfo 1103`, `MsgWeaponSkill 1025`, `MsgFlushExp 1104`, `MsgMagicEffect 1105`) وربط خطوتي الدخول `LoginProficiencies (77)` و `LoginSpells (78)` في `MsgAction`.
  - [x] نظام الحالات والبافات الكامل `StatusSet` (`1UL << (statusId - 1)`) شاملاً دائرة الـ XP التلقائية (`START_XP`), `Cyclone`, `Superman`, `Stigma`, `Shield / MagicShield`, `StarOfAccuracy`, `Invisibility`, `Fly`, `Intensify`, وحالات النينجا (`PoisonStar` المانعة للجرعات، `ToxicFog` الدورية، `ShurikenVortex`، و `FatalStrike`) مع تمديد وقت الـ KO عند قتل الوحوش.
  - [x] محرك `MagicData` الشامل لكل عائلات المهارات للـ 6 كلاسات حتى النينجا (`FastBlade / ScentSword` بخوارزمية Bresenham، `Scatter`، `Hercules`، `Dash`، `Roar`، `Cure / HealingRain / Revive / Pray / Meditation`، `Tornado / FireMeteor / FireOfHell`، `TwofoldBlades / ToxicFog / PoisonStar / Counter-Kill / ArcherBane / ShurikenVortex / FatalStrike`) + خبرة إتقان الأسلحة من 1 إلى 20 ومهارات الأسلحة التلقائية (`Phoenix, Snow, Boreas, Rage, Celestial, Earthquake, Seizer, Penetration...`) + كتب المهارات وأوامر `/skill`, `/allskills`, `/wskill`, `/xp`.
- [x] **المرحلة 5 (نظام الـ NPCs، التجارة، البوثات، الفريق، الأصدقاء/الأعداء، وتوحيد هوية `Mystic-Realms`) — مكتملة 100%:**
  - [x] توحيد اسم وهوية اللعبة بالكامل إلى **`Mystic-Realms`** في `GameBranding.cs`، `sql/comet.account.sql` (`realm`)، `Comet.Account`، `Comet.Game`، `Comet.AutoPatcher`، رسائل الدخول والإعلانات، وكافة حوارات الـ NPCs + ضبط `Dragon.Module.Connect.json` (`enable_redirect: true`).
  - [x] جداول `cq_npc` (458 NPC رسمي لباتش 5187 بعد التنقية وإضافة `MysticGuide` في قرية الميلاد والتاون سيتي + `MarketEscort`)، `cq_dynanpc`، `cq_friend`، و `cq_enemy` في `sql/comet.game.sql` (21 جدولاً إجمالاً) + نماذج ومستودعات EF Core مطابقة 100%.
  - [x] الكيانات `Npc`, `DynamicNpc`, `BoothNpc`, `NpcManager` والباكتات `MsgNpcInfo (2030)`, `MsgNpcInfoEx (1109)`, `MsgNpc (2031)`, `MsgTaskDialog (2032)` مع حماية كاملة ضد تزوير أرقام الاختيارات (`Option Spoofing Protection`).
  - [x] محرك `NpcScriptEngine` بـ C# (`[NpcScript]` + `NpcDialog`) شاملاً مرشدي `MysticGuide`، محولات المدن والماركت (`Conductress` مع حفظ نقطة العودة من الماركت)، مدربي الترقيات والمهارات للـ 6 كلاسات حتى النينجا، أساتذة الريبورن الأول والثاني، حدادي السوكت والتطوير، البنوك، الحلاق، ومأمور السجن.
  - [x] نظام التجارة الآمنة (`Trade.cs` + `MsgTrade 1056` للفضة والـ CPs والأيتمات)، بوثات الماركت (`BoothNpc.cs` + `MsgItemInfoEx 1108` للفضة والـ CPs ونداء البائع)، البارتي (`Team.cs` + `MsgTeam 1023` + `MsgTeamMember 1026` + نجمة القائد ومشاركة الخبرة)، ونظام الأصدقاء والأعداء التلقائي عند الـ PK (`RelationshipManager.cs` + `MsgFriend 1019`).
- [ ] **المرحلة 6 (النقابات، الأرسنال، العائلات، التوب، والأرينا):**
  - [ ] نظام الـ **Nobility / Peerage** (`MsgPeerage 2064` — King, Prince, Duke, Earl).
  - [ ] نظام الـ **Syndicates / Guilds** كامل (`MsgSyndicate 1107`) + الـ **Totem Pole / Arsenal** (`MsgTotemPole 2201-2203`) + حرب النقابات **Guild War** (`MsgNpcInfoEx` للأبواب والعمود).
  - [ ] نظام الـ **Clans / Families** (`MsgFamily 1312`, `MsgFamilyOccupy 1313`).
  - [ ] نظام الـ **Arena Qualifier 1v1** (`MsgQualifying* 2205-2211`).
  - [ ] الـ Quiz Show، الـ Flowers، الـ Offline TG، والـ Broadcast/Pigeon (`MsgPigeon 2050`).
- [ ] **المرحلة 7 (المراجعة النهائية، الحماية، وتجهيز الداتابيز للافتتاح):**
  - [ ] مراجعة شاملة ضد ثغرات الباكتات والتدبيل (Anti-Exploit & Validation).
  - [ ] تجهيز ملفات SQL الأساسية وأدوات الإدارة والتجربة الفعلية على الكلينت.

---

## 7. سجل التعديلات (Session Changelog)
* **2026-10-25 (المرحلة 0):**
  * تهيئة المستودع بسورس **Comet 5187** الخام النظيف (`Comet.Account`, `Comet.Game`, `Comet.Network`, `Comet.Core`, `Comet.Shared`, `sql/`, `tests/`).
  * إضافة كود **Dragon Launcher** في `tools/DragonLauncher/` (لتوجيه اتصال الكلينت وحل مشكلة الفلاش).
  * إنشاء مشروع **Comet.AutoPatcher** بـ C# في `tools/Comet.AutoPatcher/`.
  * تحديث `.gitignore` لحماية مساحة الـ Workspace من الملفات التنفيذية والأرشيفات الضخمة.
  * إنشاء `AGENT_HANDOFF.md` وتحديث `README.md` والرفع على GitHub فرع `arena/3db48bb9-redux-classic`.
* **2026-10-25 (المرحلة 1 — نواة العالم والخرائط والحركة والرؤية والشات):**
  * توحيد إصدار لغة C# إلى `LangVersion 10` عبر جميع مشاريع `.NET 6.0` وإضافة `Comet.Shared/Log.cs` واختبارات الوحدات في `Comet.Core.Tests`.
  * إصلاح ثغرات وأخطاء الهيكل الخام في `Comet.Account` (`Server.Disconnected`, `MsgAccount` فحص الحظر والصيانة وتسجيل `LoginsRepository`) و `Comet.Game` (`MsgConnect` شرط الـ IP، نقل إدارة الجلسات إلى `RoleManager`، وتصحيح إنشاء التاويست والولادة في قرية الميلاد `1010: 61, 109` في `MsgRegister`).
  * إضافة `TimeOut`, `TimeOutMS` بـ `Environment.TickCount64` و `ScreenCalculations` في `Comet.Core`.
  * بناء محرك المعالجة المتوازي الخالي من الأقفال `WorldProcessor` وخدمة التوقيت `WorldTimerService`.
  * بناء منظومة الخرائط الرسمية (`Tile`, `GameMapData`, `GameBlock`, `Weather`, `GameMap`, `Screen`, `MapManager`, `ExperienceManager`) مع داتا كونكر الرسمية في `sql/comet.game.sql` ونماذج EF Core (`DbMap`, `DbPassway`, `DbPortal`, `DbRegion`, `DbLevExp`, `DbPointAllot`).
  * بناء الكيانات (`Role`, `Character`) وكافة باكتات العالم والحركة لباتش 5187 بدقة البايت (`MsgPlayer 10014`, `MsgWalk 10005`, `MsgAction 10010`, `MsgUserAttrib 10017`, `MsgMapInfo 1110`, `MsgWeather 1016`, `MsgData 1033`, `MsgPing 1037`, `MsgName 1015`, `MsgAllot 1024`, `MsgTalk 1004`) ونظام الأوامر `CommandManager`.
  * فحص شامل لكامل شجرة الـ AST والربط ومطابقة أنواع SQL/EF Core لـ 105 ملف C# بنتيجة **0 أخطاء**.
* **2026-10-25 (المرحلة 2 — نظام الأيتمات، الشنطة، المعدات، البنوك، الشوبات، التطوير والسوكتات):**
  * إضافة `TqDatCrypto` (`0x2537`) لفك تشفير `ini/itemtype.dat` تلقائياً، وبناء `ItemManager` المزدوج (`cq_itemtype` / `cq_itemaddition` / `cq_goods` + `itemtype.dat` / `ItemAdd.ini` / `Shop.dat`) مع أمر `/syncitemsdb`.
  * بناء `Item`, `UserPackage`, `MapItem` مع قواعد اللبس الرسمية لباتش 5187 (شاملة النينجا كاتانا المزدوجة، التليزمانات، الحصنة وألوانها، الـ Battle Power، وقفل المعدات بمهلة 5 أيام).
  * بناء وربط باكتات `MsgItemInfo (1008)`, `MsgItem (1009)`, `MsgMapItem (1101)`, `MsgPackage (1102)`, `MsgGemEmbed (1027)`, `MsgDataArray (2036)`, `MsgEquipLock (2048)` ومحرك سكريبتات `ItemScriptEngine` (`[ItemScript]`).
  * إضافة معدات البداية للاعبين الجدد في `MsgRegister` وأوامر `/item`, `/clearinv`, `/syncitemsdb` في `CommandManager`.
  * توثيق الشرح المفصل للمبتدئين ومتطلبات الكلينت في `قسم معلومات التعديلات/` وفحص شامل لـ 125 ملف C# و140 Type و11 جدول SQL بنتيجة **0 أخطاء**.
* **2026-10-25 (المرحلة 3 — الوحوش، الحراس، الإسباون الرسمي، الدروب، القتال الأساسي، والريبورن):**
  * استخراج وتنقية بيانات شركة TQ الرسمية لباتش 5187 داخل `sql/comet.game.sql` (`437 KB` إجمالاً، 14 جدولاً): `cq_monstertype` (579 صفاً × 46 عموداً بعد إزالة أعمدة 5517)، `cq_generator` (1,377 صفاً × 14 عموداً)، و `cq_rebirth` (72 صفاً للكلاسات الـ 6 حتى النينجا بدون Monk/Pirate).
  * بناء نماذج ومستودعات `DbMonstertype`, `DbGenerator`, `DbRebirth` وتسجيلها في `ServerDbContext`.
  * بناء `Monster` (مع طابور إعادة تدوير UIDs لمنع استنزاف الأرقام، وذكاء اصطناعي للحراس والوحوش يتوقف تلقائياً في الـ Blocks الخالية من اللاعبين، ومزامنة شاشة دقيقة)، `Generator`, `MonsterManager`, `DropManager` (مع فهرسة المعدات القابلة للسقوط حسب لفل الوحش في `ItemManager`), و `RebirthManager` (الريبورن الأول والثاني ومنحنى النقاط الإضافية والـ Enlightenment).
  * بناء وتسجيل باكت `MsgInteract (1022)` في `Server.cs` (يشمل الضرب الجسدي التلقائي، رمي القوس وخصم الأسهم، فك تشفير `MagicAttack` لباتش 5187، الموت كشبح وإعادة الإحياء `Revive` في `Character` و `MsgAction`)، وتحديث `MsgPlayer` بباني الوحوش `MsgPlayer(Monster)`، وإضافة أوامر `/spawnmob`, `/rebirth`, `/rates`, `/revive`.
  * توثيق المرحلة 3 بالكامل في `قسم معلومات التعديلات/` وفحص شامل لـ 137 ملف C# و14 جدول SQL بنتيجة **0 أخطاء**.
* **2026-10-25 (المرحلة 4 — نظام المهارات `MagicType`، خبرة الأسلحة `Weapon Skills`، وسكيلات الـ 6 شخصيات حتى النينجا):**
  * استخراج وتنقية **803 صف رسمي** لجدول `cq_magictype` (35 عموداً لباتش 5187 بدون Monk/Pirate) وإضافة جدولي `cq_magic` و `cq_weapon_skill` إلى `sql/comet.game.sql` (`588 KB` إجمالاً، 17 جدولاً) + نماذج ومستودعات EF Core وتسجيلها في `ServerDbContext`.
  * بناء الباكتات الأربعة الرسمية لباتش 5187 (`MsgMagicInfo 1103`, `MsgMagicEffect 1105`, `MsgWeaponSkill 1025`, `MsgFlushExp 1104`) وربط `LoginProficiencies (77)` و `LoginSpells (78)` في `MsgAction`.
  * بناء `StatusSet` (حالات 5187 بقاعدة `1UL << (id - 1)` شاملة البافات والسموم الدورية ودائرة `START_XP` وتمديد وقت الـ KO)، `MagicManager` (قارئ مزدوج لـ `ini/magictype.dat` + `cq_magictype`)، و `MagicData` (كافة مهارات الكلاسات الـ 6 حتى النينجا شاملة `TwofoldBlades`, `ToxicFog`, `PoisonStar` المانعة للجرعات، `Counter-Kill`, `ArcherBane`, `ShurikenVortex`, `FatalStrike`, `FastBlade/ScentSword`, `Scatter`, `Fly`, `Intensify`, `Revive`, `Pray`, `Stigma`, `Cyclone`, `Superman`, وخبرة ومهارات الأسلحة التلقائية).
  * دعم كتب المهارات في `ItemScriptEngine` وإضافة أوامر `/skill`, `/allskills`, `/wskill`, `/xp` في `CommandManager`.
  * توثيق المرحلة 4 في `قسم معلومات التعديلات/` وفحص شامل لـ 150 ملف C# و17 جدول SQL بنتيجة **0 أخطاء**.
* **2026-10-25 (المرحلة 5 — الـ NPCs، محرك الحوارات، التجارة، البوثات، البارتي، الأصدقاء/الأعداء، وتوحيد هوية `Mystic-Realms`):**
  * إنشاء `Comet.Shared/GameBranding.cs` وتغيير هوية السيرفر بالكامل والـ `realm` في `sql/comet.account.sql` والأوتوباتشر وكافة رسائل السيرفر وحوارات الـ NPCs إلى **`Mystic-Realms`**.
  * إضافة جداول `cq_npc` (458 صف رسمي لباتش 5187)، `cq_dynanpc`، `cq_friend`، و `cq_enemy` إلى `sql/comet.game.sql` (`665 KB` إجمالاً، 21 جدولاً) + نماذج ومستودعات EF Core وتسجيلها في `ServerDbContext`.
  * بناء `Npc`, `DynamicNpc`, `BoothNpc`, `NpcManager`, `NpcScriptEngine` (`NpcDialog` + `[NpcScript]`), `Trade`, `Team`, `RelationshipManager` والباكتات الـ 9 (`MsgNpcInfo 2030`, `MsgNpcInfoEx 1109`, `MsgNpc 2031`, `MsgTaskDialog 2032`, `MsgTrade 1056`, `MsgTeam 1023`, `MsgTeamMember 1026`, `MsgFriend 1019`, `MsgItemInfoEx 1108`) وربطها بالكامل في `Character`, `Monster`, `MsgAction`, `MsgItem`, `MsgTalk`, `Server`, `Program`.
  * توثيق المرحلة 5 ودليل تغيير اسم اللعبة في الكلينت داخل `قسم معلومات التعديلات/`، وفحص شامل لـ 176 ملف C# و21 جدول SQL بنتيجة **0 أخطاء**.
* **2026-10-26 (تشغيل السيرفر فعلياً على ويندوز وإصلاحات الدخول لباتش 5187 — PRs `#4` إلى `#7` `[Build v5.3]`):**
  * **PR `#4` (`3dc9af9`):** إصلاح كافة أخطاء بناء Visual Studio 2022 (`0 Errors`).
  * **PR `#5` (`59c511b`):** مطابقة قنوات `MsgTalk` لباتش 5187 (`Register = 2100`, `Login = 2101`)، الحفاظ على التوكن في `MsgConnect` عبر مرحلة إنشاء الشخصية `NEW_ROLE`، جعل `MsgRegister` يحفظ الشخصية ومعداتها ومهارات التاويست المبدئية ويرد بـ `ANSWER_OK` ليعيد الكلينت الدخول نظيفاً، وإرجاع باكت `LoginSpawn (74)` في `MsgAction`.
  * **PR `#6` & `#7` (`[Build v5.3]`):** إضافة `ResolveRedirectEndpoint` وكاشف الوصول في `Remote.cs` وتنظيف تحذير `GameMapData` المضلّل عند تحميل 175+ ملف `.DMap` رسمي.
  * **PR `#8` (`[Build v5.4]`):** إيقاف تحويل عنوان هاماشي `25.x.x.x` إلى `192.168.x.x` في `MsgAccount.cs`، وتفعيل `Socket.ExclusiveAddressUse = true` في `TcpServerListener.cs`، وإضافة فحص `[SelfTest]` التلقائي عند التشغيل في `RealmsRepository.cs` + `Remote.cs` (والذي طبع `[SelfTest] PASS` وأثبت سلامة الشبكة والفايروول وبورت `5816` بنسبة 100%).
  * **PR `#9` (`[Build v5.5]`):** بمقارنة سورس `Comet 5187` الخام مع `Long V5187` (`darkfoxdeveloper/Long.Game`)، تم إصلاح الأخطاء البرمجية الثلاثة في مرحلة تسليم الكلينت من `Account` إلى `Game`: (1) تغيير توليد التوكن في `Remote.cs` من 8 بايت عشوائية إلى `uint` (4 بايت غير صفرية) حتى تكون البايتات `8..11` (`dwData`) في `MsgConnectEx` مساوية لـ `0` دائماً ولا يحاول `Conquer.exe` الاتصال بـ `BindServerIP` الفارغ في `COServer.dat`، (2) تصحيح حجم `MsgConnectEx.Encode()` إلى `32` بايت بالضبط بحذف `writer.Write((ushort)0)` الزائدة، (3) إغلاق سوكت `9960` نظيفاً (`actor.Disconnect()`) في `Comet.Account/Server.cs` فور استلام باكت `res.dat` ومطابقة أوفستات `MsgConnect.Decode` في `Comet.Game`.
  * **PR `#10` (`[Build v5.6]` — التأمين الشامل للشبكة والسيرفر والاقتصاد وتنظيف الوورك سبيس):**
    * **تأمين الـ RPC وتسجيل الدخول واتصال لاعبي الـ VPS والإنترنت العام:** قفل `RpcNetwork` على `127.0.0.1` + قائمة بيضاء `AllowedClientIPs` في `RpcServerListener`، تفعيل `StrictAuthPass: true` الذكي (يدعم كروت نفس الجهاز محلياً ويُلزم تطابق IP صارم لكل لاعب خارجي)، حماية ضد تخمين الباسورد (Brute-Force) في `MsgAccount`، دعم خوادم Cloud/Router NAT في `[SelfTest]`، ضبط `TcpServerRegistry` (`maxActiveConn=30, maxAttempts=60, banMinutes=2`) لدعم شبكات CGNAT والسايبرات وتعدد الكلينتات بدون حظر خاطئ، إغلاق مقابس `Graceful Shutdown` آمن في `TcpServerActor`، وحماية `PacketProcessor` و `TcpServerListener` من سقوط الخيوط أو تسريب `MaxConn`.
    * **إصلاح `MsgUserInfo (1006)` لباتش 5187:** تصحيح البايت `70` (`NameDisplayed = 1`) والبايت `83` (`VIPLevel`) والسترينجات عند `89`.
    * **سد ثغرات التدبيل والنصب وصلاحيات الإدارة (Anti-Dupe & Anti-Theft):** حماية `ulong` للـ Silver والـ CPs وحفظ فوري `SaveAsync` وسجل `Logs/Economy` في `Trade` و `BoothNpc`، قفل تعديل الأيتمات أثناء التجارة أو العرض بالبوث في `MsgItem`/`MsgPackage`/`MsgGemEmbed`/`MsgDataArray`، سحب الحصان الفرعي قبل إضافة نقاط التركيب في `MsgDataArray`، حصر المخازن في الـ 7 الرسمية + فحص مسافة الـ NPC للشوبات والمخازن، منع إعادة إرسال باكتات الدخول `MsgAction 74..78` أثناء اللعب، فصل رتب الإدارة (`2=Moderator`, `3=GM`, `4..5=PM/Admin`) مع سجل مراقبة `Logs/GmAudit` وحذف حساب `Spirited` الافتراضي من `comet.account.sql`.
    * **تنظيف الوورك سبيس:** إزالة كافة الملفات الزائدة القديمة (`.dockerignore`, `.gitlab-ci.yml`, `.vscode/`, `compose.*.yml`, `dockerfile`, `doc/`, `sql/upgrades/`, `src/*/Readme.md`, `tools/DragonLauncher/CONTRIBUTING.md`) مع الحفاظ على حجم المشروع النظيف (`~3.1 MB`) وصفر أخطاء في 174 ملف C#.




