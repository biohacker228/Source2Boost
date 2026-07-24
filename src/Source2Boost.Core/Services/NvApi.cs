using System.Runtime.InteropServices;

namespace Source2Boost.Core;

/// <summary>
/// Тонкая обёртка над NVAPI (nvapi64.dll) для записи глобального профиля драйвера NVIDIA
/// (то, что в «Панели управления NVIDIA» → «Управление параметрами 3D»). Ставит игровые
/// значения: питание = максимум производительности, низкая задержка (1 предрендер-кадр),
/// верт. синхронизация = выкл, кэш шейдеров = вкл.
///
/// БЕЗОПАСНОСТЬ: NVAPI проверяет поле version у структуры настройки. Если размер/версия не
/// совпали — драйвер вернёт ошибку (INCOMPATIBLE_STRUCT_VERSION) и НИЧЕГО не запишет, порчи
/// профиля не будет. Все вызовы проверяются на NVAPI_OK; любой сбой = аккуратный откат/false.
/// Доступ к функциям — через nvapi_QueryInterface(id) → указатель на функцию (NVAPI не
/// экспортирует их напрямую).
/// </summary>
public static class NvApi
{
    // ---- ID функций для nvapi_QueryInterface (стабильные «магические» числа) ----
    private const uint ID_Initialize        = 0x0150E828;
    private const uint ID_Unload            = 0xD22BDD7E;
    private const uint ID_DRS_CreateSession = 0x0694D52E;
    private const uint ID_DRS_DestroySession= 0xDAD9CFF8;
    private const uint ID_DRS_LoadSettings  = 0x375DBD6B;
    private const uint ID_DRS_SaveSettings  = 0xFCBC7E14;
    private const uint ID_DRS_GetBaseProfile= 0xDA8466A0;
    private const uint ID_DRS_SetSetting    = 0x577DD202;
    private const uint ID_DRS_GetSetting    = 0x73BF8338;

    // ---- ID настроек драйвера (из официального NvApiDriverSettings.h) ----
    private const uint PREFERRED_PSTATE_ID   = 0x1057EB71; // питание
    private const uint PRERENDERLIMIT_ID     = 0x007BA09E; // макс. предрендер-кадров (низкая задержка)
    private const uint VSYNCMODE_ID          = 0x00A879CF; // верт. синхронизация
    private const uint PS_SHADERDISKCACHE_ID = 0x00198FFF; // кэш шейдеров на диске
    private const uint TEXFILTER_QUALITY_ID  = 0x00CE2691; // фильтрация текстур — качество

    // Значения «игровые» и «по умолчанию»
    private const uint PSTATE_PREFER_MAX = 0x00000001, PSTATE_ADAPTIVE = 0x00000000;
    private const uint PRERENDER_LOWLATENCY = 0x00000001, PRERENDER_APP = 0x00000000;
    private const uint VSYNC_FORCEOFF = 0x08416747, VSYNC_PASSIVE = 0x60925292;
    private const uint SHADERCACHE_ON = 0x00000001;
    private const uint TEXFILTER_HIGHPERF = 0x00000014, TEXFILTER_QUALITY = 0x00000000;

    // ---- Раскладка NVDRS_SETTING (v1). Все u32 по 4-байтовым смещениям. ----
    // version | name[4096] | settingId | type | location | isCurPredef | isPredefValid | predefUnion[4100] | curUnion[4100]
    private const int OFF_ID = 4 + 4096;      // 4100
    private const int OFF_TYPE = OFF_ID + 4;  // 4104
    private const int OFF_LOCATION = OFF_TYPE + 4; // 4108
    private const int OFF_CUR_VALUE = 4 + 4096 + 4 + 4 + 4 + 4 + 4 + 4100; // 8220
    private const int SETTING_SIZE = OFF_CUR_VALUE + 4100; // 12320
    private static readonly uint SETTING_VER = (uint)SETTING_SIZE | (1u << 16);

    private const int NVAPI_OK = 0;

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    private delegate int Fn0();
    private delegate int FnPtr(IntPtr a);
    private delegate int FnOutPtr(out IntPtr a);
    private delegate int FnGetBaseProfile(IntPtr session, out IntPtr profile);
    private delegate int FnSetSetting(IntPtr session, IntPtr profile, IntPtr setting);
    private delegate int FnGetSetting(IntPtr session, IntPtr profile, uint settingId, IntPtr setting);

    private static T? Get<T>(uint id) where T : Delegate
    {
        var p = QueryInterface(id);
        return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    // Доступность драйвера в рамках сессии не меняется — кэшируем, чтобы не грузить DLL при
    // каждом вызове (IsSupported дёргается на каждую пересборку списка твиков).
    private static bool? _available;

    /// <summary>Есть ли рабочий NVAPI (драйвер NVIDIA установлен и инициализировался).</summary>
    public static bool IsAvailable()
    {
        if (_available is bool cached) return cached;
        try
        {
            var init = Get<Fn0>(ID_Initialize);
            if (init is null || init() != NVAPI_OK) return (_available = false).Value;
            Get<Fn0>(ID_Unload)?.Invoke();
            return (_available = true).Value;
        }
        catch { return (_available = false).Value; }
    }

    /// <summary>Применить (enable=true) игровой профиль или вернуть к значениям по умолчанию (false).</summary>
    public static bool SetGamingProfile(bool enable)
    {
        var settings = enable
            ? new (uint id, uint val)[]
            {
                (PREFERRED_PSTATE_ID, PSTATE_PREFER_MAX),
                (PRERENDERLIMIT_ID, PRERENDER_LOWLATENCY),
                (VSYNCMODE_ID, VSYNC_FORCEOFF),
                (PS_SHADERDISKCACHE_ID, SHADERCACHE_ON),
                (TEXFILTER_QUALITY_ID, TEXFILTER_HIGHPERF),
            }
            : new (uint id, uint val)[]
            {
                (PREFERRED_PSTATE_ID, PSTATE_ADAPTIVE),
                (PRERENDERLIMIT_ID, PRERENDER_APP),
                (VSYNCMODE_ID, VSYNC_PASSIVE),
                (PS_SHADERDISKCACHE_ID, SHADERCACHE_ON),
                (TEXFILTER_QUALITY_ID, TEXFILTER_QUALITY),
            };
        return WithBaseProfile((set, session, profile) =>
        {
            foreach (var (id, val) in settings)
                if (set(session, profile, id, val) != NVAPI_OK) return false;
            return true;
        }, save: true);
    }

    /// <summary>Применён ли игровой профиль (питание = максимум И низкая задержка = 1 кадр).</summary>
    public static bool IsGamingProfileApplied()
        => ReadSetting(PREFERRED_PSTATE_ID) == PSTATE_PREFER_MAX
           && ReadSetting(PRERENDERLIMIT_ID) == PRERENDER_LOWLATENCY;

    /// <summary>Dev-проверка ЗАПИСИ: трогает только PRERENDER и сразу возвращает к исходному
    /// значению — подтверждает, что SetSetting работает, не меняя живой профиль пользователя.</summary>
    public static string WriteRoundTripTest()
    {
        if (!IsAvailable()) return "nvapi: unavailable";
        var before = ReadSetting(PRERENDERLIMIT_ID);           // может быть null (не задано)
        bool wrote = WithBaseProfile((set, s, p) => set(s, p, PRERENDERLIMIT_ID, PRERENDER_LOWLATENCY) == NVAPI_OK, save: true);
        var after = ReadSetting(PRERENDERLIMIT_ID);
        // восстановить: если было значение — вернуть его, иначе app-controlled (0)
        uint restore = before ?? PRERENDER_APP;
        WithBaseProfile((set, s, p) => set(s, p, PRERENDERLIMIT_ID, restore) == NVAPI_OK, save: true);
        return $"nvapi write-test: wrote={wrote} before={(before is null ? "unset" : $"0x{before:X8}")} " +
               $"afterWrite={(after is null ? "read-failed" : $"0x{after:X8}")} restoredTo=0x{restore:X8}";
    }

    /// <summary>Неразрушающая диагностика: доступность + текущие значения (для проверки корректности
    /// размера структуры на реальной карте). Ничего не пишет.</summary>
    public static string Diag()
    {
        if (!IsAvailable()) return "nvapi: unavailable";
        string R(uint id) { var v = ReadSetting(id); return v is null ? "read-failed" : $"0x{v:X8}"; }
        return $"nvapi: available | PSTATE={R(PREFERRED_PSTATE_ID)} PRERENDER={R(PRERENDERLIMIT_ID)} " +
               $"VSYNC={R(VSYNCMODE_ID)} SHADERCACHE={R(PS_SHADERDISKCACHE_ID)} TEXFILTER={R(TEXFILTER_QUALITY_ID)} " +
               $"| structSize={SETTING_SIZE} ver=0x{SETTING_VER:X8}";
    }

    // ---- Внутреннее: открыть сессию + базовый профиль, выполнить действие, (опц.) сохранить ----
    private delegate int SetFn(IntPtr session, IntPtr profile, uint id, uint val);

    private static bool WithBaseProfile(Func<SetFn, IntPtr, IntPtr, bool> action, bool save)
    {
        var init = Get<Fn0>(ID_Initialize);
        var unload = Get<Fn0>(ID_Unload);
        var create = Get<FnOutPtr>(ID_DRS_CreateSession);
        var destroy = Get<FnPtr>(ID_DRS_DestroySession);
        var load = Get<FnPtr>(ID_DRS_LoadSettings);
        var saveFn = Get<FnPtr>(ID_DRS_SaveSettings);
        var getBase = Get<FnGetBaseProfile>(ID_DRS_GetBaseProfile);
        var setSetting = Get<FnSetSetting>(ID_DRS_SetSetting);
        if (init is null || create is null || load is null || getBase is null || setSetting is null) return false;

        IntPtr session = IntPtr.Zero;
        try
        {
            if (init() != NVAPI_OK) return false;
            if (create(out session) != NVAPI_OK) return false;
            if (load(session) != NVAPI_OK) return false;
            if (getBase(session, out var profile) != NVAPI_OK) return false;

            SetFn set = (s, p, id, val) =>
            {
                var buf = Marshal.AllocHGlobal(SETTING_SIZE);
                try
                {
                    for (int i = 0; i < SETTING_SIZE; i++) Marshal.WriteByte(buf, i, 0);
                    Marshal.WriteInt32(buf, 0, (int)SETTING_VER);
                    Marshal.WriteInt32(buf, OFF_ID, (int)id);
                    Marshal.WriteInt32(buf, OFF_TYPE, 0);       // NVDRS_DWORD_TYPE
                    Marshal.WriteInt32(buf, OFF_LOCATION, 0);   // NVDRS_CURRENT_PROFILE_LOCATION
                    Marshal.WriteInt32(buf, OFF_CUR_VALUE, (int)val);
                    return setSetting(s, p, buf);
                }
                finally { Marshal.FreeHGlobal(buf); }
            };

            var ok = action(set, session, profile);
            if (ok && save) ok = saveFn is not null && saveFn(session) == NVAPI_OK;
            return ok;
        }
        catch { return false; }
        finally
        {
            if (session != IntPtr.Zero) destroy?.Invoke(session);
            unload?.Invoke();
        }
    }

    private static uint? ReadSetting(uint settingId)
    {
        var init = Get<Fn0>(ID_Initialize);
        var unload = Get<Fn0>(ID_Unload);
        var create = Get<FnOutPtr>(ID_DRS_CreateSession);
        var destroy = Get<FnPtr>(ID_DRS_DestroySession);
        var load = Get<FnPtr>(ID_DRS_LoadSettings);
        var getBase = Get<FnGetBaseProfile>(ID_DRS_GetBaseProfile);
        var getSetting = Get<FnGetSetting>(ID_DRS_GetSetting);
        if (init is null || create is null || load is null || getBase is null || getSetting is null) return null;

        IntPtr session = IntPtr.Zero;
        var buf = Marshal.AllocHGlobal(SETTING_SIZE);
        try
        {
            if (init() != NVAPI_OK) return null;
            if (create(out session) != NVAPI_OK) return null;
            if (load(session) != NVAPI_OK) return null;
            if (getBase(session, out var profile) != NVAPI_OK) return null;
            for (int i = 0; i < SETTING_SIZE; i++) Marshal.WriteByte(buf, i, 0);
            Marshal.WriteInt32(buf, 0, (int)SETTING_VER);
            if (getSetting(session, profile, settingId, buf) != NVAPI_OK) return null;
            return (uint)Marshal.ReadInt32(buf, OFF_CUR_VALUE);
        }
        catch { return null; }
        finally
        {
            Marshal.FreeHGlobal(buf);
            if (session != IntPtr.Zero) destroy?.Invoke(session);
            unload?.Invoke();
        }
    }
}
