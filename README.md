# Запись экрана

Портативная программа для Windows: записывает весь экран, звук всех приложений и голос с микрофона в один файл MP4. Установка и права администратора не нужны.

## Как запустить

1. Скачайте [ffmpeg essentials](https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip) и положите `ffmpeg.exe` из папки `bin` рядом с `ZapisEkrana.exe`.
2. Запустите `ZapisEkrana.exe`.
3. «Захват экрана» начинает запись, «Стоп» сохраняет файл и открывает его.

Папку с программой можно держать на рабочем столе, в документах или на флешке. В «Program Files» её класть не нужно.

Пока в настройках не выбрана другая папка, записи сохраняются в папку «Записи» рядом с программой. Там же выбираются микрофон и экран, если мониторов несколько.

Файл записи — MP4 (H.264 + AAC), до 30 кадров в секунду. Размер кадра берётся из текущего разрешения экрана. Если экран шире 1920 пикселей, картинка пропорционально уменьшается до этой ширины.

## Сборка

Нужен Windows 10 или 11. Отдельный .NET ставить не нужно.

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /t:winexe /optimize+ /platform:x64 /codepage:65001 /win32manifest:src\app.manifest /out:portable\ZapisEkrana.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll src\Program.cs src\SettingsStore.cs src\Monitors.cs src\Audio.cs src\Recorder.cs src\Ui.cs
```

`ffmpeg.exe` в репозиторий не входит: готовая сборка больше лимита GitHub на размер файла. Это отдельная программа под лицензией GPL, её сборки публикует [gyan.dev](https://www.gyan.dev/ffmpeg/builds/).

## Лицензия

Исходный код этой программы — MIT, см. [LICENSE](LICENSE).
