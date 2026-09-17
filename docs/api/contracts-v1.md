# Contratos da API v1

> Status: 🔒 FROZEN v1 — contratos aprovados para implementação
> Data: 2026-09-17

Base: `/api/v1`. JSON, UUID, ISO-8601. `TenantId` vem exclusivamente do contexto autenticado/confiável e não é aceito do cliente como autoridade. Requisições propagam `X-Correlation-Id`.

## 0. Convenções globais

### Normalização e validação
A API aceita entradas amigáveis, normaliza apenas o que for tecnicamente seguro, valida estrutura e regras antes de persistir e não corrige silenciosamente dados semanticamente inválidos.

Fluxo: `Request -> Normalização segura -> Validação estrutural -> Validação de domínio -> Application/Domain -> Persistência`.

- nomes/descrições: trim e normalização de espaços, preservando apresentação;
- e-mail: trim, validação e representação apropriada para comparação;
- telefone: representação canônica, preferencialmente E.164;
- CPF/CNPJ: remover máscara, validar e aplicar a estratégia de proteção/fingerprint definida na arquitetura;
- códigos canônicos de catálogo: gerados/normalizados pelo backend;
- preço: decimal positivo e precisão monetária apropriada;
- duração: inteiro positivo em minutos;
- UUID: formato válido, existência e pertencimento ao contexto permitido;
- instantes: ISO-8601 e persistência `timestamptz`;
- horários recorrentes: hora local interpretada no timezone da `Location`;
- enums: somente valores conhecidos pela versão da API.

Normalização técnica pertence à fronteira de aplicação; invariantes de negócio permanecem no Domain/Application. PostgreSQL continua protegendo invariantes críticas.

### Idempotência
Operações críticas usam `Idempotency-Key` obrigatório: criar Appointment, reagendar, cancelar, criar nova PaymentAttempt, solicitar Refund e criar ScheduleBlock. GETs não exigem chave.

A identidade idempotente considera conceitualmente `TenantId + Operation + IdempotencyKey`, associada a hash do request e resultado. Mesma chave + mesmo payload retorna o mesmo resultado sem repetir o efeito. Mesma chave + payload diferente retorna `409 IDEMPOTENCY_CONFLICT`.

Idempotência da API não substitui proteção de concorrência do banco. Webhooks usam identidade própria do provider (`Provider + ProviderEventId`). Operações em gateways também propagam chave idempotente própria quando suportado.

### Problem Details e HTTP Status
Erros seguem RFC 9457 Problem Details, estendido com `code`, `correlationId` e, quando aplicável, `errors`.

```json
{
  "type": "https://api.virtualemployee.com/problems/slot-unavailable",
  "title": "Horário indisponível",
  "status": 409,
  "code": "SLOT_UNAVAILABLE",
  "detail": "O horário selecionado não está mais disponível.",
  "correlationId": "01K5..."
}
```

Convenção: `400` request estruturalmente inválido; `401` não autenticado; `403` sem autorização; `404` recurso inexistente dentro do contexto permitido; `409` concorrência/conflito de estado/idempotência; `422` validação/regra de domínio; `429` rate limit; `500` falha interna; `502/503` dependência externa quando apropriado.

Tentativas de acessar recurso de outro Tenant não revelam existência e são tratadas como `404 RESOURCE_NOT_FOUND`. Respostas de erro nunca expõem stack trace, SQL, segredos ou detalhes sensíveis de providers; diagnóstico interno é correlacionado via `correlationId`/App Insights. `code` é o contrato estável para clientes; mensagens podem evoluir/localizar.

## 1. Business Types e Business
`GET /business-types?search=bar&limit=20`

`POST /business-types`
```json
{"name":"Podologia"}
```

`BusinessType` é catálogo global reutilizável. O cliente não define o `code`; o backend normaliza/gera o código canônico, verifica duplicidade e permite ao Tenant continuar o onboarding. Novos tipos dinâmicos ficam sujeitos a curadoria antes de serem expostos como opção geral para outros Tenants, evitando poluição do catálogo.

Exemplo de resposta:
```json
{"id":"<uuid>","code":"PODOLOGY","name":"Podologia","isActive":true}
```

`GET /business`, `PUT /business`. Business mantém nome, BusinessType, políticas e SlotIntervalMinutes. Endereço/timezone pertencem à Location.

## 2. Locations
`GET /locations?active=true`
`GET /locations/{locationId}`
`POST /locations`
`PUT /locations/{locationId}`

MVP cria uma Location no onboarding, mas contratos não assumem que ela será sempre única.

## 3. Services, Combos e disponibilidade por Location
`GET /services?active=true&locationId=<uuid>`
`POST /services`, `PUT /services/{serviceId}`.

Service pertence ao Business e é a fonte comercial de preço/duração no MVP. `serviceType` é `SINGLE` ou `COMBO` e é imutável após criação; cadastro incorreto deve ser desativado e recriado.

SINGLE:
```json
{"name":"Corte Masculino","serviceType":"SINGLE","price":50.00,"durationMinutes":30}
```

COMBO:
```json
{
  "name":"Corte + Barba",
  "serviceType":"COMBO",
  "price":80.00,
  "durationMinutes":60,
  "componentServiceIds":["<corte-id>","<barba-id>"]
}
```

COMBO possui preço/duração próprios. Todos os componentes devem existir, pertencer ao mesmo Business, ser `SINGLE` e não podem ser duplicados. COMBO nunca contém outro COMBO. Violação de nesting retorna `422 COMBO_NESTING_NOT_ALLOWED`. Componentes podem ser alterados enquanto as invariantes forem preservadas; snapshots históricos de AppointmentItem permanecem autoritativos para bookings existentes.

`PUT /locations/{locationId}/services/{serviceId}`
```json
{"isActive":true}
```

`LocationService` define somente disponibilidade do Service na unidade. A API v1 não aceita nem retorna override de preço/duração por Location. Preço/duração por unidade não fazem parte da Migration 001.

## 4. Professionals, Locations e Services
`GET /professionals?active=true&locationId=<uuid>&serviceIds=<id1>,<id2>`
`POST /professionals`, `PUT /professionals/{professionalId}`.

Professional pertence ao Business.

`PUT /professionals/{professionalId}/locations`
```json
{"locationIds":["<moema-id>","<tatuape-id>"]}
```

`ProfessionalLocation` define onde trabalha; `ProfessionalService` define o que executa. Quando vários serviceIds forem informados, retornar somente profissionais da Location habilitados para TODOS os Services. Para COMBO, elegibilidade é explícita em `ProfessionalService`; não é inferida pelos componentes.

## 5. Availability Rules
`GET /locations/{locationId}/professionals/{professionalId}/availability-rules`

`PUT /locations/{locationId}/professionals/{professionalId}/availability-rules`
```json
{"rules":[{"dayOfWeek":1,"startTime":"09:00","endTime":"12:00"},{"dayOfWeek":1,"startTime":"13:00","endTime":"18:00"}]}
```

AvailabilityRule é específica de Professional + Location. Timezone vem da Location.

## 6. Schedule Blocks
`GET /schedule-blocks?locationId=<uuid>&from=<instant>&to=<instant>&professionalId=<uuid>`

`POST /schedule-blocks/impact` recebe LocationId, ProfessionalId opcional e intervalo. ProfessionalId null bloqueia a Location inteira. Criar block não move appointments automaticamente. A operação de criação efetiva do block requer `Idempotency-Key`.

## 7. Customers
`GET /customers`, `GET /customers/{id}`, `POST /customers`.

## 8. Booking Quote
`POST /booking/quote`
```json
{"locationId":"<uuid>","serviceIds":["<corte-id>","<barba-id>"]}
```

Backend valida que todos os Services estão disponíveis na Location. Quote é informativo; CreateAppointment recalcula tudo. Preço/duração vêm de Service (ou do próprio COMBO selecionado), sem override por Location no MVP.

## 9. Available Slots
`POST /availability/slots/search`
```json
{"locationId":"<moema-id>","serviceIds":["<corte-id>","<barba-id>"],"date":"2026-09-18","professionalId":null}
```

Busca: LocationService -> ProfessionalLocation -> ProfessionalService -> AvailabilityRule -> ScheduleBlock -> Appointments.

## 10. Appointments
`GET /appointments?locationId=<uuid>`
`GET /appointments/{appointmentId}`

`POST /appointments` requer `Idempotency-Key`.
```json
{
  "customerId":"<uuid>",
  "locationId":"<uuid>",
  "professionalId":"<uuid>",
  "serviceIds":["<corte-id>","<barba-id>"],
  "startsAt":"2026-09-20T14:00:00-03:00"
}
```

Create não aceita preço, duração, `endsAt` ou status autoritativos. Backend revalida Customer, Location, Services na Location, ProfessionalLocation, ProfessionalServices, AvailabilityRules, ScheduleBlocks, Appointments e concorrência; calcula preço/duração/EndsAt e cria Appointment + AppointmentItems com snapshots comerciais.

Resposta de criação usa `201 Created`, retorna `Location` do recurso e os valores derivados/snapshots, incluindo `status=PENDING`, `totalPrice`, `totalDurationMinutes`, `reservationExpiresAt` e items.

### Pagamento integral obrigatório e expiração
No MVP não existe sinal, adiantamento, pagamento parcial nem reservar para pagar depois. Todo Appointment criado para reserva comercial inicia `PENDING`, ocupa agenda e somente se torna `CONFIRMED` após pagamento integral confirmado pelo provider.

`Payment.Amount == Appointment.TotalPriceSnapshot` para pagamento de serviço. O tempo de reserva é configurável; não é hardcoded no contrato. Ao atingir `reservationExpiresAt` sem pagamento confirmado, worker materializa `EXPIRED`, liberando o slot.

Fluxo: `PENDING -> Payment -> PaymentAttempt -> webhook confirmado -> Payment CONFIRMED -> Appointment CONFIRMED`.

`CONFIRMED_BY_CLIENT` representa confirmação posterior de presença pelo cliente, processada pelo backend após interpretação do canal; IA não altera estado diretamente.

### Double-booking
`409 SLOT_UNAVAILABLE`. Tentativa perdedora não persiste Appointment. A proteção considera o Professional globalmente dentro do Tenant, evitando reserva simultânea em duas Locations. Idempotência não substitui a exclusion constraint do PostgreSQL.

### Reschedule
`POST /appointments/{id}/reschedule` requer `Idempotency-Key`. Pode mudar Location quando elegibilidade/disponibilidade forem satisfeitas. Se futura diferença comercial exigir ajuste financeiro, MVP usa cancelamento/novo booking; sem pagamento complementar/refund parcial.

### Cancel
`POST /appointments/{id}/cancel` requer `Idempotency-Key`.

Estados operacionais previstos: `PENDING`, `CONFIRMED`, `CONFIRMED_BY_CLIENT`, `RESCHEDULE_REQUESTED`, `CANCELLED_BY_CLIENT`, `CANCELLED_BY_BUSINESS`, `COMPLETED`, `NO_SHOW`, `EXPIRED`. Reschedule não cria estado permanente `RESCHEDULED`; mantém histórico/evento e retorna ao estado operacional apropriado.

## 11. Payments e PaymentAttempts
Payment representa a obrigação financeira lógica do Appointment; PaymentAttempt representa cada tentativa concreta no provider. Um Payment pode possuir N PaymentAttempts.

Criar nova tentativa:
```http
POST /appointments/{appointmentId}/payment-attempts
Idempotency-Key: <uuid>
```
```json
{"paymentMethod":"PIX"}
```

Métodos permitidos para pagamento de serviço: `PIX`, `CREDIT_CARD`, `DEBIT_CARD`. Sem sinal, parcial ou boleto. O cliente nunca envia o valor. Backend valida Appointment `PENDING` e não expirado, obtém/cria o único Payment lógico SERVICE do Appointment, garante `Payment.Amount == Appointment.TotalPriceSnapshot` e cria a PaymentAttempt.

Resposta de criação da tentativa usa `201 Created` e pode retornar `paymentId`, `paymentAttemptId`, `paymentMethod`, `status`, `amount`, `checkoutUrl`/referência segura e `expiresAt`. Dados de cartão nunca transitam por IA, WhatsApp ou nossa API; checkout é hospedado/tokenizado pelo provider.

Uma tentativa PIX pode expirar e ser seguida por cartão ou novo PIX sem criar outro Payment. Mesma intenção repetida usa idempotência; nova escolha consciente de pagamento usa nova Idempotency-Key e nova PaymentAttempt.

Estados Payment: `PENDING`, `PROCESSING`, `CONFIRMED`, `FAILED`, `CANCELLED`, `REFUNDED`.

Estados PaymentAttempt: `CREATED`, `PENDING`, `PROCESSING`, `CONFIRMED`, `FAILED`, `EXPIRED`, `CANCELLED`.

Somente PaymentAttempt confirmada por evento/API confiável do provider pode liquidar o Payment. Redirect de browser é informativo e nunca confirma pagamento. Se Payment já estiver confirmado, nova tentativa é rejeitada com `422 PAYMENT_ALREADY_CONFIRMED`.

### Webhook e confirmação financeira
Webhook valida autenticidade/assinatura conforme provider e é deduplicado por `Provider + ProviderEventId`. Processamento é idempotente e atualiza PaymentAttempt -> Payment -> Appointment somente quando a transição for válida.

Se confirmação financeira chegar após Appointment estar `EXPIRED`, a verdade financeira é registrada (`PaymentAttempt CONFIRMED`, `Payment CONFIRMED`), mas Appointment permanece `EXPIRED`. O sistema nunca reativa o slot e dispara Refund compensatório integral.

## 12. Refunds
`POST /payments/{paymentId}/refunds` requer `Idempotency-Key`.
```json
{"reason":"CUSTOMER_CANCELLATION"}
```

Refund é integral no MVP. O cliente não envia `amount`; backend determina `Refund.Amount = Payment.Amount`, que para SERVICE corresponde ao `Appointment.TotalPriceSnapshot`.

Backend valida Tenant, Payment `CONFIRMED`, PaymentAttempt efetivamente `CONFIRMED`, política de cancelamento e inexistência de outro estorno integral efetivo/em processamento incompatível. Refund referencia Payment e a PaymentAttempt que originou a transação externa.

Solicitação aceita retorna `202 Accepted` com estado inicial `REFUND_REQUESTED`. Processamento é assíncrono: `REFUND_REQUESTED -> REFUND_PROCESSING -> REFUNDED | REFUND_FAILED`. Nunca comunicar refund concluído antes da confirmação do provider.

Cancelamento causado pelo estabelecimento segue fluxo de estorno integral quando houver pagamento. Cancelamento pelo cliente respeita política configurada; quando houver direito a estorno, ele continua integral no MVP.

Retries usam a mesma Idempotency-Key e, quando disponível, idempotência do provider. O objetivo é no máximo um estorno integral efetivo, mesmo com timeout, retry ou webhook duplicado.

Pagamento confirmado tardiamente após Appointment `EXPIRED` dispara este mesmo mecanismo como Refund compensatório integral; o Appointment permanece expirado.

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

LLM não acessa EF Core/DB diretamente e não confirma pagamento/refund.

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
COMBO_NESTING_NOT_ALLOWED
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
INTERNAL_ERROR
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

Segunda fatia: Payment + PaymentAttempts + webhook + Refunds. Terceira: Schedule Blocks/WhatsApp/AI/Analytics/Billing/Usage.

## 19. Freeze v1
API Contracts v1 congelado em 2026-09-17 após aprovação dos contratos críticos de Appointment, Payment/PaymentAttempt e Refund e revisão de consistência com o ERD v1 congelado. Não foi identificada necessidade de alteração estrutural do ERD.

Novas ideias não críticas entram no backlog. Mudanças incompatíveis posteriores exigem decisão explícita e evolução/versionamento do contrato; não reabrir informalmente a baseline v1.
