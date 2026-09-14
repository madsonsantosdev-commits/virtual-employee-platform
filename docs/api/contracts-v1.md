# Contratos da API v1

> Status: Draft v1 — alinhado às regras aprovadas
> Data: 2026-09-14

Base: `/api/v1`. JSON, UUID, ISO-8601. TenantId vem do contexto confiável. Escritas críticas aceitam `Idempotency-Key`; requisições propagam `X-Correlation-Id`.

## 1. Business Types e Business
`GET /business-types?search=bar&limit=20`
`POST /business-types` com `{ "name": "Studio de sobrancelhas" }`.

`GET /business`, `PUT /business`. Business mantém nome, BusinessType, políticas e SlotIntervalMinutes. Endereço/timezone pertencem à Location.

## 2. Locations
`GET /locations?active=true`
`GET /locations/{locationId}`
`POST /locations`
`PUT /locations/{locationId}`

Exemplo:
```json
{
  "name":"Moema",
  "legalEntityId":"<uuid-or-null>",
  "phone":"+5511999999999",
  "addressLine1":"Av. Exemplo",
  "number":"100",
  "district":"Moema",
  "city":"São Paulo",
  "state":"SP",
  "postalCode":"00000000",
  "countryCode":"BR",
  "timezone":"America/Sao_Paulo"
}
```

MVP cria uma Location no onboarding, mas contratos não assumem que ela será sempre única.

## 3. Services, Combos e disponibilidade por Location
`GET /services?active=true&locationId=<uuid>`
`POST /services`, `PUT /services/{serviceId}`.

Service pertence ao Business e contém preço/duração padrão.

`PUT /locations/{locationId}/services/{serviceId}`
```json
{"isActive":true}
```

`LocationService` define disponibilidade por unidade. Overrides de preço/duração ficam preparados como evolução e não entram na primeira UX/API.

COMBO possui preço/duração próprios, componentes SINGLE e sem nesting.

## 4. Professionals, Locations e Services
`GET /professionals?active=true&locationId=<uuid>&serviceIds=<id1>,<id2>`
`POST /professionals`, `PUT /professionals/{professionalId}`.

Professional pertence ao Business.

`PUT /professionals/{professionalId}/locations`
```json
{"locationIds":["<moema-id>","<tatuape-id>"]}
```

`ProfessionalLocation` define onde trabalha; `ProfessionalService` define o que executa.

Quando vários serviceIds forem informados, retornar somente profissionais da Location habilitados para TODOS os Services.

## 5. Availability Rules
`GET /locations/{locationId}/professionals/{professionalId}/availability-rules`

`PUT /locations/{locationId}/professionals/{professionalId}/availability-rules`
```json
{
  "rules":[
    {"dayOfWeek":1,"startTime":"09:00","endTime":"12:00"},
    {"dayOfWeek":1,"startTime":"13:00","endTime":"18:00"}
  ]
}
```

AvailabilityRule é específica de Professional + Location. Timezone vem da Location.

## 6. Schedule Blocks
`GET /schedule-blocks?locationId=<uuid>&from=<instant>&to=<instant>&professionalId=<uuid>`

`POST /schedule-blocks/impact`
```json
{
  "locationId":"<uuid>",
  "professionalId":null,
  "startsAt":"2026-09-18T12:00:00Z",
  "endsAt":"2026-09-18T21:00:00Z"
}
```

ProfessionalId null bloqueia a Location inteira. Criar block não move appointments automaticamente.

## 7. Customers
`GET /customers`, `GET /customers/{id}`, `POST /customers`.

## 8. Booking Quote
`POST /booking/quote`
```json
{
  "locationId":"<uuid>",
  "serviceIds":["<corte-id>","<barba-id>"]
}
```

Backend valida que todos os Services estão disponíveis na Location. Quote é informativo; CreateAppointment recalcula tudo.

## 9. Available Slots
`POST /availability/slots/search`
```json
{
  "locationId":"<moema-id>",
  "serviceIds":["<corte-id>","<barba-id>"],
  "date":"2026-09-18",
  "professionalId":null
}
```

Response conceitual:
```json
{
  "locationId":"<moema-id>",
  "date":"2026-09-18",
  "timezone":"America/Sao_Paulo",
  "totalPrice":170.00,
  "totalDurationMinutes":75,
  "slots":[{
    "professionalId":"<arthur-id>",
    "professionalName":"Arthur",
    "startsAt":"2026-09-18T13:00:00Z",
    "endsAt":"2026-09-18T14:15:00Z",
    "localStartsAt":"2026-09-18T10:00:00-03:00"
  }]
}
```

Busca: LocationService -> ProfessionalLocation -> ProfessionalService -> AvailabilityRule -> ScheduleBlock -> Appointments.

## 10. Appointments
`GET /appointments?locationId=<uuid>`
`GET /appointments/{appointmentId}`
`POST /appointments` requer Idempotency-Key.

```json
{
  "locationId":"<moema-id>",
  "customerId":"<uuid>",
  "professionalId":"<arthur-id>",
  "serviceIds":["<corte-id>","<barba-id>"],
  "startsAt":"2026-09-18T13:00:00Z"
}
```

Não aceitar preço, duração ou EndsAt autoritativos. Backend revalida Location, Services na Location, ProfessionalLocation, ProfessionalServices, agenda, blocks e concorrência.

Response inclui `locationId`, Professional, Services, snapshots, totais, status e ReservationExpiresAt.

### Double-booking
`409 SLOT_UNAVAILABLE`. Tentativa perdedora não persiste Appointment. A proteção considera o Professional globalmente dentro do Tenant, evitando que o mesmo profissional seja reservado simultaneamente em duas Locations.

### Reschedule
`POST /appointments/{id}/reschedule`
```json
{
  "locationId":"<uuid>",
  "professionalId":"<uuid>",
  "startsAt":"2026-09-19T13:00:00Z"
}
```

Pode mudar Location quando regras de elegibilidade/disponibilidade forem satisfeitas. Se futura diferença de preço exigir ajuste financeiro, MVP usa cancelamento/novo booking; sem pagamento complementar/refund parcial.

### Cancel
`POST /appointments/{id}/cancel` requer Idempotency-Key.

## 11. Payments
`POST /payments`
```json
{"appointmentId":"<uuid>","paymentMethod":"PIX"}
```
Valor vem de Appointment.TotalPriceSnapshot. PIX, CREDIT_CARD, DEBIT_CARD. Sem sinal, parcial ou boleto. Checkout hospedado/tokenizado; redirect não confirma pagamento.

Webhook validado/idempotente confirma pagamento e Appointment.

## 12. Refunds
`POST /payments/{paymentId}/refunds` — integral no MVP; confirmação apenas após provider.

## 13. Analytics
Endpoints financeiros aceitam `locationId` opcional para visão por unidade:
`GET /analytics/financial-summary?locationId=<uuid>`
`GET /analytics/revenue-by-professional?locationId=<uuid>`
`GET /analytics/revenue-by-service?locationId=<uuid>`
`GET /analytics/forecast?locationId=<uuid>`

## 14. SaaS Billing
Billing separado de Customer Payments. Subscription associa unidades faturáveis explicitamente via SubscriptionUnit/Location.

Mensal: RECURRING_CARD ou PIX. Anual: RECURRING_CARD. Sem boleto.

## 15. Usage
`GET /usage/summary?locationId=<uuid>` quando o consumo puder ser atribuído a uma unidade. Custos globais continuam tenant-scoped.

## 16. WhatsApp / Conversation
`POST /integrations/whatsapp/webhook`.

```text
WhatsApp Adapter -> Messaging -> Conversation Engine -> AI Gateway -> Application tools -> Domain
```

LLM não acessa EF Core/DB diretamente.

## 17. Códigos de domínio
```text
VALIDATION_ERROR
UNAUTHORIZED
FORBIDDEN
RESOURCE_NOT_FOUND
LOCATION_NOT_AVAILABLE
SERVICE_NOT_AVAILABLE_AT_LOCATION
PROFESSIONAL_NOT_AVAILABLE_AT_LOCATION
PROFESSIONAL_NOT_ELIGIBLE_FOR_SERVICE
INVALID_COMBO
COMBO_COMPONENT_INVALID
SLOT_UNAVAILABLE
APPOINTMENT_NOT_CANCELABLE
APPOINTMENT_NOT_RESCHEDULABLE
APPOINTMENT_EXPIRED
PAID_APPOINTMENT_SERVICE_CHANGE_NOT_ALLOWED
PAYMENT_NOT_ALLOWED
PAYMENT_ALREADY_CONFIRMED
PAYMENT_PROVIDER_ERROR
REFUND_NOT_ALLOWED
REFUND_ALREADY_COMPLETED
IDEMPOTENCY_CONFLICT
RATE_LIMIT_EXCEEDED
```

## 18. Primeira fatia vertical
```text
GET Locations
GET/POST Services
PUT LocationService
GET/POST Professionals
PUT ProfessionalLocations
PUT AvailabilityRules(Location,Professional)
POST /booking/quote
POST /availability/slots/search
POST /appointments
GET /appointments/{id}
POST /appointments/{id}/reschedule
POST /appointments/{id}/cancel
```

Segunda fatia: Payments + webhook + Refunds. Terceira: Schedule Blocks/WhatsApp/AI/Analytics/Billing/Usage.
