# خطوات تجهيز كلينت 5187 والاتصال بسيرفر MoonQueen (Client Setup Guide)

هذا الدليل يوضح الإعدادات المطلوبة داخل مجلد كلينت **Conquer Online 5187** لديك (`5187`) لكي يتصل بسيرفر **`MoonQueen`** على آي بي الهاماشي **`25.21.212.130`**.

---

## 1. إعدادات اللودر (`ConquerLoader`)

إذا كنت تستخدم **ConquerLoader** داخل فولدر كلينت `5187`، تأكد من ضبط إعدادات السيرفر كالتالي:
- **Server Name (اسم السيرفر):** `MoonQueen`
- **Login Host / IP:** `25.21.212.130`
- **Game Host / IP:** `25.21.212.130`
- **Login Port:** `9960`
- **Game Port:** `5816`
- **Client Version / Patch:** `5187`

*(ملاحظة مهمة: حتى لو كان اسم السيرفر في `Server.dat` مختلفاً عن `MoonQueen`، برمجنا سيرفر الحسابات في `MsgAccount.cs` ليوجهك تلقائياً إلى سيرفر `MoonQueen` حتى لا تواجه رسالة خطأ بسبب اختلاف الاسم!).*

---

## 2. السماح للبورتات في جدار الحماية (Windows Firewall) لدخول أصدقائك عبر Hamachi

لكي يدخل معك أي لاعب على نفس شبكة **Hamachi** (`25.21.212.130`):
1. عند تشغيل `MoonQueen.Game.exe` و `MoonQueen.Account.exe` لأول مرة، سيظهر لك إشعار **Windows Defender Firewall**؛ ضع علامة صح على **Private networks** و **Public networks** واضغط **Allow access**.
2. أو افتح **Command Prompt (مسؤول / Run as Administrator)** ونفّذ هذين الأمرين مرة واحدة فقط لفتح بورتي السيرفر (`9960` و `5816`):
   ```cmd
   netsh advfirewall firewall add rule name="MoonQueen Account 9960" dir=in action=allow protocol=TCP localport=9960
   netsh advfirewall firewall add rule name="MoonQueen Game 5816" dir=in action=allow protocol=TCP localport=5816
   ```
   *(لا تفتح بورت `5817` في الجدار الناري لأنه بورت داخلي محمي بين السيرفرين على `127.0.0.1` فقط).*

---

## 3. تجربة الدخول الأولى
1. شغّل `MoonQueen.Game` ثم `MoonQueen.Account`.
2. افتح اللعبة من اللودر وسجّل الدخول بالحساب الجاهز:
   - **Username:** `admin`
   - **Password:** `123456`
3. ستفتح لك شاشة إنشاء الشخصية (`NEW_ROLE`) — اختر اسم شخصيتك وأي تخصص (`Trojan / Warrior / Archer / Ninja / Taoist`) واضغط **OK**.
4. سيكتمل شريط التحميل إلى **100%** وتنزل بشخصيتك في منتصف **Twin City (`430, 380`)** وتظهر لك رسالة الترحيب الخاصة بسيرفر **`MoonQueen`**!
