// Заглушка нативной части com.unity.dt.app-ui (транзитивная зависимость com.unity.ai.assistant).
// Её libAppUINativePlugin.a собрана только под устройство, а AppUI зовёт NativeAppUI_Initialize
// при старте плеера — без заглушки симуляторная сборка либо не линкуется, либо падает на запуске.
// На устройстве файл пуст, линкуется настоящая библиотека.
#include <TargetConditionals.h>
#if TARGET_OS_SIMULATOR
#include <stdint.h>

extern "C" {
struct AppUIColor { float r, g, b, a; };

int32_t   NativeAppUI_Initialize(void*) { return 1; }
void      NativeAppUI_Uninitialize() {}
void      NativeAppUI_Update() {}
int32_t   NativeAppUI_DarkMode() { return 0; }
int32_t   NativeAppUI_HighContrast() { return 0; }
int32_t   NativeAppUI_ReduceMotion() { return 0; }
int32_t   NativeAppUI_LayoutDirection() { return 0; }
float     NativeAppUI_ScaleFactor() { return 1.0f; }
float     NativeAppUI_TextScaleFactor() { return 1.0f; }
AppUIColor NativeAppUI_GetSystemColor(int32_t) { return {0, 0, 0, 1}; }
void      NativeAppUI_RunHapticFeedback(int32_t) {}
uintptr_t NativeAppUI_GetPasteBoardDataLength(int32_t) { return 0; }
void      NativeAppUI_GetPasteBoardData(int32_t, uintptr_t, intptr_t) {}
void      NativeAppUI_SetPasteBoardData(int32_t, uintptr_t, intptr_t) {}
}
#endif
