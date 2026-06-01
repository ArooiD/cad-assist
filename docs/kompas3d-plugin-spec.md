# Спецификация плагина CAD Assist для КОМПАС-3D

## 1. Назначение

CAD Assist — прикладной модуль для КОМПАС-3D, который связывает элементы CAD-модели с внешними инженерными объектами и IoT-устройствами.

Плагин должен работать как помощник конструктора: пользователь выбирает объект модели, назначает ему устройство, видит статус и может выгрузить отчёт по связям.

## 2. MVP

В первой версии реализуются:

1. Настройка адреса backend API.
2. Авторизация по логину и паролю.
3. Загрузка списка устройств.
4. Выбор устройства в интерфейсе плагина.
5. Создание привязки к CAD-объекту или координате.
6. Хранение привязок в JSON-файле рядом с моделью.
7. Просмотр списка привязок.
8. Экспорт CSV-отчёта.

## 3. База реализации

Для быстрого MVP используется Windows/.NET-приложение с модульной архитектурой. Интеграция с КОМПАС-3D вынесена в отдельный адаптер `IKompasCadAdapter`.

Такой подход позволяет быстро реализовать бизнес-логику, UI, API-клиент и хранение, а затем заменить mock-адаптер на реальную интеграцию с КОМПАС-3D API.

## 4. Компоненты

```text
CAD Assist
├── UI
├── Application Services
├── Backend API Client
├── Binding Storage
├── CAD Adapter
└── Reports
```

## 5. Модель данных

### DeviceDto

```json
{
  "id": "cabinet-1",
  "name": "Шкаф 1",
  "type": "cabinet",
  "status": "normal",
  "lastTelemetryAt": "2026-05-28T12:00:00Z"
}
```

### CadBinding

```json
{
  "id": "guid",
  "cadSystem": "KOMPAS-3D",
  "modelPath": "C:/models/example.a3d",
  "cadObjectId": "mock-object-1",
  "deviceId": "cabinet-1",
  "deviceName": "Шкаф 1",
  "deviceType": "cabinet",
  "x": 0,
  "y": 0,
  "z": 0,
  "createdAt": "2026-05-28T12:00:00Z"
}
```

## 6. Backend contract

Минимально ожидаемые endpoints:

```http
POST /api/auth/login
GET  /api/devices
GET  /api/devices/{id}/telemetry/latest
```

Для разработки предусмотрен mock-режим, если backend недоступен.

## 7. Реализация CAD Adapter

В MVP используется `MockKompasCadAdapter`, который возвращает тестовый выбранный объект и путь модели. Это нужно, чтобы не блокировать разработку UI и бизнес-логики отсутствием установленного КОМПАС-3D.

Позже он заменяется на `KompasCadAdapter`, который будет работать через API КОМПАС-3D.

## 8. Экспорт отчёта

CSV-отчёт должен содержать:

- BindingId;
- CAD system;
- Model path;
- CAD object id;
- Device id;
- Device name;
- Device type;
- X/Y/Z;
- CreatedAt.
