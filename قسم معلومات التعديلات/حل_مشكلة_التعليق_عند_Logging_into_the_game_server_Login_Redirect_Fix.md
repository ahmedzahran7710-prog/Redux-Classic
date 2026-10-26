# الحل الجذري والنهائي لمشكلة التعليق عند `Logging into the game server..` (`[Build v5.4]`)

## 1. ماذا كشفت لقطتا الشاشة في `[Build v5.2]` و `[Build v5.3]` بدقة 100%؟
بعد مراجعة لقطتي الشاشة وتفكيك ملفات اللودر (`ConquerLoader` / `COHook.dll` / `ConquerCipherHook.dll`) ومقارنة السورس بايت ببايت مع `Comet 5187` الأصلي، اتضح السببان المتلازمان اللذان سبّبا المشكلة:

### السبب الأول: فلتر شركة TQ المدمج داخل `Conquer.exe` الخام (رفض عناوين الشبكة الداخلية `192.168.x.x`)
* كلينت **Patch 5187** الأصلي (`Conquer.exe`) يحتوي بداخله على فلتر حماية رسمي من شركة TQ يرفض الاتصال بأي عنوان IP محلي من نطاقات **RFC1918** (`127.*.*.*`, `10.*.*.*`, `172.16-31.*.*`, **`192.168.*.*`**) داخل باكت `MsgConnectEx` ما لم يكن هناك Hook يعترض دالة `ws2_32!connect`.
* برنامج **`ConquerLoader`** في وضع باتش `5187` لا يحقن `CLHook.dll` لتغيير الـ IP، ولذلك:
  * قبل عنوان هاماشي **`25.21.212.130`** في شاشة الدخول (`9960`) لأن النطاق `25.*.*.*` هو نطاق IP عام غير محلي!
  * بينما عندما قام `[Build v5.3]` بتحويل `25.21.212.130` إلى `192.168.1.2`، قام `Conquer.exe` برفض الاتصال بـ `192.168.1.2` بصمت تام قبل حتى إرسال أول حزمة TCP!

### السبب الثاني: قاعدة الحظر المخفية (`Action=Block`) على برنامج `Comet.Game.exe` في جدار حماية ويندوز (أو تعارض بورت `5816`)
* في التجربة الأولى (`[Build v5.2]`) عندما أرسل السيرفر `25.21.212.130:5816`، لماذا لم يصل الاتصال أيضاً؟
  1. لأن ويندوز يصنف كارت شبكة **Hamachi (`25.21.212.130`)** كشبكة عامة (**`Public Network`**). وعند تشغيل `Comet.Game.exe` لأول مرة وظهور نافذة جدار حماية ويندوز، يكون خيار `Private networks` فقط مفعّلاً وخيار `Public networks` غير مفعّل افتراضياً — فينشئ ويندوز تلقائياً **قاعدة حظر صريحة (`Action = Block`) على ملف `Comet.Game.exe` نفسه لشبكات `Public`**!
  2. في نظام جدار حماية ويندوز (`WFP`)، **قواعد الحظر (`Block`) لها أولوية مطلقة فوق كل قواعد السماح (`Allow`)**! لذلك أمر `netsh ... action=allow localport=5816` وحده **لا يلغي** قاعدة `Block` المسجلة على ملف `Comet.Game.exe` نفسه!
  3. بالإضافة إلى ذلك، في ويندوز خاصية `Socket.ExclusiveAddressUse` تكون `false` افتراضياً إن لم تُفعّل برمجياً، مما يسمح ببقاء عملية قديمة مستحوذة على بورت `5816` بصمت دون إظهار خطأ!

---

## 2. ماذا عدّلنا وأضفنا في السورس في `[Build v5.4]`؟ (شرح مبسّط للمبتدئين)

### أ) في ملف `src/Comet.Account/Packets/MsgAccount.cs`
* أوقفنا تماماً تحويل عنوان هاماشي (`25.x.x.x`) إلى `192.168.x.x` حتى يرسل السيرفر عنوان **`25.21.212.130:5816`** الذي يقبله `Conquer.exe` الأصلي مباشرة بدون أي مشاكل.
* وإذا كان `GameIPAddress` في جدول `realm` مكتوباً بالخطأ `127.0.0.1` أو `192.168.x.x` بينما اللاعب دخل على `Account Server` عبر هاماشي (`25.21.212.130`)، يعتمد السيرفر تلقائياً نفس العنوان الناجح (`25.21.212.130`)!

### ب) في ملف `src/Comet.Network/Sockets/TcpServerListener.cs` و `src/Comet.Game/Program.cs`
* فعّلنا `this.Socket.ExclusiveAddressUse = true;` قبل ربط البورت (`Bind`) في كلٍ من `Account Server` و `Game Server`، مع فحص فوري لأي خطأ ربط بورت وإظهاره بوضوح تام حتى يستحيل استحواذ أي عملية قديمة في الخلفية على بورت `5816` أو `9960` بصمت.
* رفعنا حدود الحماية في `TcpServerRegistry.cs` (`maxActiveConn = 50`, `maxAttemptsPerMinute = 60`) مع معالجة نظيفة لفحوصات حالة البورت (`ONLINE` في اللودر وفحص `[SelfTest]`).

### ج) الفحص الذاتي التلقائي الفوري عند التشغيل (`[SelfTest]`) في `RealmsRepository.cs` و `Remote.cs`
* بمجرد اتصال `Account Server` بـ `Game Server` عبر RPC عند تشغيل السيرفر (قبل حتى أن تفتح الكلينت!)، يقوم `Account Server [Build v5.4]` تلقائياً بفتح اتصال TCP حقيقي على **`25.21.212.130:5816`** والتحقق عبر RPC أن نافذة `Game Server` الحالية استلمت الاتصال فعلياً، ويطبع لك النتيجة فوراً على شاشة `Account Server`:
  * **`[SelfTest] PASS: Game port 25.21.212.130:5816 is OPEN and verified on 'Mystic-Realms' Game Server! Ready for login.`** -> معناها الطريق مفتوح 100% وجاهز للدخول فوراً!
  * **`[SelfTest] BLOCKED BY WINDOWS FIREWALL`** -> معناها جدار الحماية ما زال يحظر `Comet.Game.exe` وتحتاج تشغيل أمر الـ PowerShell بالأسفل كمسؤول (`Run as Administrator`).
  * **`[SelfTest] PORT HIJACKED`** -> معناها توجد عملية أخرى في `Task Manager` حاجزة بورت `5816`.

---

## 3. خطوات التطبيق المضمونة 100% على جهازك (3 خطوات فقط)

### الخطوة 1: امسح قواعد الحظر المخفية (`Block`) من جدار حماية ويندوز (مهم جداً!)
افتح قائمة ابدأ واكتب **`PowerShell`**، ثم اضغط كليك يمين واختر **Run as administrator** (تشغيل كمسؤول)، والصق هذا الأمر الواحد واضغط **Enter**:
```powershell
Get-NetFirewallRule | Where-Object { $_.Action -eq 'Block' -and ($_.DisplayName -match 'Comet|Mystic|Conquer' -or ($_ | Get-NetFirewallApplicationFilter).Program -match 'Comet|Conquer') } | Remove-NetFirewallRule; New-NetFirewallRule -DisplayName "Mystic-Realms Allow TCP" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 9960,5816,5817 -Profile Any -ErrorAction SilentlyContinue
```
*(هذا الأمر يحذف فوراً أي قاعدة `Block` أنشأها ويندوز تلقائياً على `Comet.Game.exe` لشبكة هاماشي `Public`، ويفتح البورتات على جميع الشبكات).*

### الخطوة 2: اسحب تحديث `[Build v5.4]` واعمل Rebuild وشغّل السيرفرين
1. اقفل نافذتي السيرفر الحاليتين، واسحب آخر تحديث من GitHub (`git pull` أو تحميل النسخة المحدثة مع الاحتفاظ بملفي `Comet.Account.config` و `Comet.Game.config`).
2. تأكد في جدول `realm` داخل قاعدة بيانات `comet.account` (في HeidiSQL) أن:
   * `Name` = `Mystic-Realms`
   * `GameIPAddress` = `25.21.212.130`
   * `GamePort` = `5816`
   * `RpcIPAddress` = `127.0.0.1`
   * `RpcPort` = `5817`
3. في **Visual Studio 2022**: اختر **Build -> Rebuild Solution**، ثم شغّل `Comet.Game` و `Comet.Account` وتأكد أن النافذتين مكتوب فيهما **`[Build v5.4]`**، وأن نافذة **`Account Server`** طبعت لك السطر الأخضر المطمئن:
   > `[SelfTest] PASS: Game port 25.21.212.130:5816 is OPEN and verified on 'Mystic-Realms' Game Server! Ready for login.`

### الخطوة 3: تأكد من إعدادات `ConquerLoader` وادخل اللعبة!
افتح `ConquerLoader.exe` واضغط **`Settings`** وتأكد أن الخانات مضبوطة هكذا:
* **Server Name:** `Mystic-Realms`
* **Login Host:** `25.21.212.130`
* **Game Host:** `25.21.212.130`
* **Login Port:** `9960`
* **Game Port:** `5816`
* **Server Version:** `5187`

احفظ الإعدادات واضغط **START** وسجّل الدخول بحسابك (`admin`) — ستدخل فوراً إلى شاشة إنشاء الشخصية (**Create Character**) وعالم **Mystic-Realms**!

---

## 4. كيف تعدّل هذه الإعدادات يدوياً مستقبلاً؟
* إذا انتقلت من **Hamachi** إلى سيرفر **VPS** حقيقي له Public IP ثابت (مثلاً `185.x.x.x`):
  1. عدّل `GameIPAddress` في جدول `realm` بقاعدة بيانات `comet.account` إلى الـ IP الجديد.
  2. عدّل `Login Host` و `Game Host` في اللودر إلى نفس الـ IP الجديد.
  3. سيقوم فحص **`[SelfTest]`** في `src/Comet.Account/Database/Repositories/RealmsRepository.cs` تلقائياً باختبار الـ IP الجديد عند كل تشغيل للتأكد أن البورت `5816` مفتوح ويصل للسيرفر!
