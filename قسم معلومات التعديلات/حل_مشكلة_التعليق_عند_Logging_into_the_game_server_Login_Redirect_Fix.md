# الحل النهائي لمشكلة التعليق عند `Logging into the game server..` (`[Build v5.5]`)

## 1. كيف كشف فحص `[SelfTest] PASS` السبب البرمجي الحقيقي في باكت `MsgConnectEx`؟
في تجربة **`[Build v5.4]`** ظهر في نافذة `Account Server` الدليل الحاسم:
```text
[SelfTest] PASS: Game port 25.21.212.130:5816 is OPEN and verified on 'Mystic-Realms' Game Server! Ready for login.
```
هذا السطر أثبت قاطعاً أن **الشبكة وهاماشي وجدار الحماية وبورت `5816` سليمة ومفتوحة 100%**، وأن المشكلة ليست من جهازك ولا من الفايروول، بل في **البايتات التي يقرأها `Conquer.exe` من باكت `MsgConnectEx (1055)`** عند تشغيله عبر **`ConquerLoader` (`COServer.dat`)**!

وبمقارنة سورس **Comet 5187** الخام سطراً بسطر مع سورس **`Long V5187`** (المطوَّر من نفس صاحب برنامج `ConquerLoader` لباتش `5187`)، وجدنا **3 اختلافات برمجية قاتلة في باكت التحويل**:

### الخلل البرمجي الأول (الرئيسي): توليد `Token` من 8 بايت عشوائية بدلاً من 4 بايت (`uint`)
* في سورس `Comet` الخام (`Remote.cs`): كان يولّد `Token` بطول **8 بايت (`ulong`)** عشوائية بالكامل، مما يجعل البايتات من `8` إلى `11` (`dwData`) داخل `MsgConnectEx` تحمل قيماً عشوائية غير صفرية دائماً!
* في كلينت باتش **5187** عند استخدام ملف `COServer.dat` المُولَّد بواسطة `ConquerLoader` (والذي يترك حقول `<field name="BindServerIP" />` فارغة و `<field name="BindServerPort">0</field>`):
  * عندما تكون قيمة `dwData` (البايتات `8..11`) **لا تساوي صفراً**، يعتبر `Conquer.exe` الحساب خاضعاً لنظام ربط سيرفر الحماية (`BindServer`) فيحاول الاتصال بـ `BindServerIP` الفارغ (`"" : 0`) بدلاً من `25.21.212.130:5816`، فيفشل الاتصال داخلياً دون إرسال أي حزمة للسيرفر!
* **الإصلاح في `src/Comet.Game/Remote.cs`:** توليد التوكن كعدد **32-بت (`uint` من 4 بايت فقط لا يساوي صفراً)** بحيث تكون البايتات `8..11` (`dwData`) في `MsgConnectEx` **دائماً `0` (`0x00000000`)** تماماً كما في `Long V5187`!

### الخلل البرمجي الثاني: طول باكت `MsgConnectEx` (`34` بايت بدلاً من `32` بايت)
* في `src/Comet.Account/Packets/MsgConnectEx.cs`: كان يكتب `writer.Write((ushort)0);` إضافية في نهاية الباكت ليصبح طوله `34` بايت بدلاً من الحجم الرسمي لهيكل `CMsgConnectEx` في باتش 5187 وهو **`32` بايت بالضبط** (`4..7 Token`, `8..11 Data=0`, `12..27 IP[16]`, `28..31 Port`).
* **الإصلاح:** إزالة الـ `2` بايت الزائدة ليطابق هيكل الـ `32` بايت الرسمي لباتش `5187`.

### الخلل البرمجي الثالث: عدم إغلاق سوكت `Account Server (9960)` بعد استلام `res.dat`
* الكلينت `Conquer.exe` يستخدم كائن شبكة واحد (`g_objNetwork`)؛ وبعد إرساله باكت تأكيد `MsgConnect ("res.dat")` إلى `Account Server`، يقوم سورس `Long V5187` بقطع اتصال بورت `9960` فوراً (`actor.Disconnect()`) لينتقل الكلينت مباشرة لبورت `5816` دون انتظار مهلة الـ 30 ثانية.
* **الإصلاح في `src/Comet.Account/Server.cs`:** استدعاء `actor.Disconnect()` مباشرة فور استلام `MsgConnect (res.dat)`، مع مطابقة أوفستات `MsgConnect.Decode` في `Comet.Game/Packets/MsgConnect.cs` لباتش `5187`.

---

## 2. كيف تطبّق تحديث `[Build v5.5]` على جهازك؟
بما أن الفايروول والشبكة عندك أصبحوا مضبوطين خلاص (`[SelfTest] PASS` ✅)، لن تحتاج لأي أوامر PowerShell جديدة! فقط:
1. اقفل نافذتي `Account Server` و `Game Server` الحاليتين.
2. اسحب آخر تحديث من GitHub (`main`) مع الاحتفاظ بملفي `Comet.Account.config` و `Comet.Game.config`.
3. في **Visual Studio 2022**: اضغط **Build -> Rebuild Solution** ثم شغّل السيرفرين وتأكد من ظهور **`[Build v5.5]`** في النافذتين وظهور `[SelfTest] PASS`.
4. ادخل من `ConquerLoader` وسجّل دخولك!

---

## 3. أماكن التعديل اليدوي في الكود (مرجع للمبتدئين)
* **تعديل هيكل باكت التوجيه للكلينت:** `src/Comet.Account/Packets/MsgConnectEx.cs` (دالة `Encode`).
* **تعديل طريقة توليد توكن الدخول ومدة صلاحيته (`60` ثانية):** `src/Comet.Game/Remote.cs` (دالة `TransferAuth`).
* **تعديل إغلاق اتصال شاشة الحساب بعد التوجيه:** `src/Comet.Account/Server.cs` (`case PacketType.MsgConnect`).
* **تعديل استقبال الكلينت عند دخوله سيرفر الجيم:** `src/Comet.Game/Packets/MsgConnect.cs`.
