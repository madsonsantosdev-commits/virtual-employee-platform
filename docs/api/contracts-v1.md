# Contratos da API v1

> Status: Draft v1 — alinhado às regras aprovadas
> Data: 2026-09-11

Base: `/api/v1`. JSON, UUID, ISO-8601. TenantId vem do contexto confiável. Escritas críticas aceitam `Idempotency-Key`; requisições propagam `X-Correlation-Id`.

## Erro padrão
```json
{
  "code": "SLOT_UNAVAILABLE",
  "message": "Desculpe, este horário acabou de ser preenchido.",
  "correlationId": "<uuid>",
  "details": null
}
```

## 1. Business Types

`GET /business-types?search=bar&limit=20`

`POST /business-types`
```json
{ "name": "Studio de sobrancelhas" }
```
Normalizar e evitar duplicação equivalente. Tipo customizado não vira automaticamente tipo oficial.

## 2. Business / Onboarding

`GET /business`

`PUT /business`
```json
{
  "name": "Barbearia Central",
  "businessTypeId": "<uuid>",
  "timezone": "America/Sao_Paulo",
  "phone": "+5511999999999",
  "slotIntervalMinutes": 15,
  "automaticRefundOnCancellation": false,
  "refundDeadlineHoursBeforeAppointment": 24
}
```

## 3. Services e Combos

`GET /services?active=true`

Resposta inclui `type` e componentes quando COMBO.

```json
{
  "id": "<uuid>",
  "name": "Corte + Barba",
  "type": "COMBO",
  "price": 120.00,
  "durationMinutes": 60,
  "requiresPayment": true,
  "componentServiceIds": ["<corte-id>", "<barba-id>"],
  "isActive": true
}
```

`POST /services`
```json
{
  "name": "Corte + Barba",
  "description": "Combo promocional",
  "type": "COMBO",
  "price": 120.00,
  "durationMinutes": 60,
  "requiresPayment": true,
  "componentServiceIds": ["<corte-id>", "<barba-id>"]
}
```

Regras: preço/duração do combo são explícitos; componentes devem ser SINGLE no MVP; sem combo aninhado; alteração não modifica snapshots históricos.

`PUT /services/{serviceId}`

`DELETE /services/{serviceId}` -> soft disable (`isActive=false`) quando houver histórico.

## 4. Professionals

`GET /professionals?active=true&serviceIds=<id1>,<id2>`

Quando vários `serviceIds` são informados, retornar profissionais habilitados para **todos** os serviços.

`POST /professionals`
```json
{
  "name": "Arthur",
  "serviceIds": ["<corte-id>", "<barba-id>"]
}
```

`PUT /professionals/{professionalId}` idem.

Regra: `ProfessionalService` é a matriz de capacidade. Um Appointment usa um único Professional no MVP.

## 5. Availability Rules / agenda-base

`GET /professionals/{professionalId}/availability-rules`

`PUT /professionals/{professionalId}/availability-rules`
```json
{
  "rules": [
    {"dayOfWeek":1,"startTime":"09:00","endTime":"12:00"},
    {"dayOfWeek":1,"startTime":"13:00","endTime":"18:00"}
  ]
}
```

A UX pode reutilizar/copiar o ciclo anterior e enviar somente a configuração final desejada, reduzindo trabalho do administrador. Não materializar slots.

## 6. Schedule Blocks e imprevistos

`GET /schedule-blocks?from=<instant>&to=<instant>&professionalId=<uuid>`

`POST /schedule-blocks/impact`
```json
{
  "professionalId": "<uuid-or-null>",
  "startsAt": "2026-09-18T12:00:00Z",
  "endsAt": "2026-09-18T21:00:00Z"
}
```

Resposta inclui appointments afetados e pode incluir alternativas calculadas.

`POST /schedule-blocks` requer Idempotency-Key.

Criar block não move appointments automaticamente. Após confirmação do administrador, Messaging/Conversation conduz clientes afetados; reagendamento exige consentimento do cliente e nova validação de slot.

`DELETE /schedule-blocks/{blockId}`.

## 7. Customers

`GET /customers`, `GET /customers/{id}`, `POST /customers`.

## 8. Booking Quote

Para suportar conversa multi-serviço sem confiar em cálculo do cliente:

`POST /booking/quote`

```json
{
  "serviceIds": ["<corte-id>", "<barba-id>"]
}
```

Response:
```json
{
  "services": [
    {"id":"<corte-id>","name":"Corte","price":80.00,"durationMinutes":45},
    {"id":"<barba-id>","name":"Barba","price":90.00,"durationMinutes":30}
  ],
  "totalPrice": 170.00,
  "totalDurationMinutes": 75,
  "comboSuggestion": {
    "serviceId": "<combo-id>",
    "name": "Corte + Barba",
    "price": 120.00,
    "durationMinutes": 60
  }
}
```

Quote é informativo; `CreateAppointment` recalcula tudo.

## 9. Available Slots

Substitui o antigo GET de um único Service:

`POST /availability/slots/search`

```json
{
  "serviceIds": ["<corte-id>", "<barba-id>"],
  "date": "2026-09-18",
  "professionalId": null
}
```

`professionalId=null`: buscar profissionais ativos habilitados para TODOS os serviços.

Response:
```json
{
  "date": "2026-09-18",
  "timezone": "America/Sao_Paulo",
  "totalPrice": 170.00,
  "totalDurationMinutes": 75,
  "slots": [
    {
      "professionalId": "<arthur-id>",
      "professionalName": "Arthur",
      "startsAt": "2026-09-18T13:00:00Z",
      "endsAt": "2026-09-18T14:15:00Z",
      "localStartsAt": "2026-09-18T10:00:00-03:00"
    }
  ]
}
```

Scheduling procura uma janela **contínua** para a duração total. `slotIntervalMinutes` define granularidade de início. Slot retornado não garante reserva.

## 10. Appointments

`GET /appointments`, `GET /appointments/{appointmentId}`.

`POST /appointments` requer Idempotency-Key.

```json
{
  "customerId": "<uuid>",
  "professionalId": "<arthur-id>",
  "serviceIds": ["<corte-id>", "<barba-id>"],
  "startsAt": "2026-09-18T13:00:00Z"
}
```

Não aceitar preço, duração ou EndsAt autoritativos.

Backend revalida Services, profissional habilitado para todos, preço, duração, agenda, blocks, intervalo completo e concorrência; então grava Appointment + AppointmentItems atomicamente.

Response 201:
```json
{
  "id": "<appointment-id>",
  "status": "PENDING",
  "professional": {"id":"<arthur-id>","name":"Arthur"},
  "startsAt": "2026-09-18T13:00:00Z",
  "endsAt": "2026-09-18T14:15:00Z",
  "reservationExpiresAt": "2026-09-18T12:10:00Z",
  "services": [
    {"id":"<corte-id>","name":"Corte","price":80.00,"durationMinutes":45},
    {"id":"<barba-id>","name":"Barba","price":90.00,"durationMinutes":30}
  ],
  "totalPrice": 170.00,
  "totalDurationMinutes": 75,
  "requiresPayment": true
}
```

### Double-booking

`409 SLOT_UNAVAILABLE`. Tentativa perdedora não persiste Appointment. `details.suggestedSlots` deve trazer alternativas quando possível.

### Reschedule

`POST /appointments/{id}/reschedule`
```json
{"professionalId":"<uuid>","startsAt":"2026-09-19T13:00:00Z"}
```

Mantém AppointmentItems. Revalida tudo e registra AppointmentHistory.

### Alteração de serviços

Fluxo preferido seleciona serviços antes do slot. Se composição precisar mudar antes do pagamento, backend deve recalcular e revalidar intervalo. Após pagamento, mudança que altere valor é recusada no MVP com `PAID_APPOINTMENT_SERVICE_CHANGE_NOT_ALLOWED`; usar cancelamento/novo booking conforme política. Sem cobrança de diferença e sem refund parcial.

### Cancel

`POST /appointments/{id}/cancel` requer Idempotency-Key. Cancelamento libera agenda sem esperar refund.

## 11. Payments — Customer Service Payment

`POST /payments`
```json
{"appointmentId":"<uuid>","paymentMethod":"PIX"}
```

O valor não é aceito do cliente. Backend usa:

```text
Appointment.TotalPriceSnapshot
```

Métodos: PIX, CREDIT_CARD, DEBIT_CARD. Sem sinal, parcial ou boleto. Card data nunca passa pela API; checkout hospedado/tokenizado. Redirect não confirma pagamento.

Webhook:
`POST /integrations/payments/{provider}/webhooks`

Validar autenticidade, deduplicar via WebhookInbox, processar idempotentemente e confirmar Appointment somente após confirmação confiável.

Pagamento recebido após Appointment `EXPIRED` não reativa reserva; iniciar compensação/refund integral.

## 12. Refunds

`POST /payments/{paymentId}/refunds` — valor integral derivado do Payment. Nunca comunicar REFUNDED antes da confirmação do provider.

## 13. Analytics

`GET /analytics/financial-summary`
`GET /analytics/revenue-by-professional`
`GET /analytics/revenue-by-service`
`GET /analytics/forecast`

AI apenas consulta Analytics Engine; não calcula valores financeiros.

## 14. SaaS Billing

Separado de Customer Payments.

Mensal: `RECURRING_CARD` ou `PIX`.
Anual/12 meses: `RECURRING_CARD`.
Sem boleto. Billing nunca desconta mensalidade das vendas dos estabelecimentos.

## 15. Usage

`GET /usage/summary` com AI requests/tokens/custos, WhatsApp inbound/outbound/custos, appointments, payments e refunds.

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
BUSINESS_RULE_VIOLATION
SERVICE_NOT_AVAILABLE
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
GET/POST Services
GET/POST Professionals
PUT AvailabilityRules
POST /booking/quote
POST /availability/slots/search
POST /appointments
GET /appointments/{id}
POST /appointments/{id}/reschedule
POST /appointments/{id}/cancel
```

Segunda fatia: Payments + webhook + Refunds. Terceira: Schedule Blocks/WhatsApp/AI/Analytics/Billing/Usage.
