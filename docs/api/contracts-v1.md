# Contratos da API v1

> Status: Draft v1
> Data: 2026-09-11

## Objetivo

Definir os contratos HTTP iniciais da Virtual Employee Platform para o MVP, alinhados ao Modelo de Domínio v1, ERD físico v1 e regras de Scheduling.

A API deve servir a PWA, integrações WhatsApp/Conversation Engine e processos internos sem expor detalhes de EF Core ou entidades persistidas.

---

## 1. Convenções gerais

Base URL:

```text
/api/v1
```

Formato:

```text
Content-Type: application/json
```

IDs:

```text
UUID
```

Datas/instantes absolutos:

```text
ISO 8601 UTC
2026-09-15T13:00:00Z
```

### Tenant

O `TenantId` deve vir do contexto autenticado/integração confiável.

Não confiar em `tenantId` enviado livremente pelo cliente para autorizar acesso.

### Correlation ID

Aceitar:

```text
X-Correlation-Id: <uuid>
```

Se ausente, a API gera um novo.

### Idempotência

Operações críticas/mutáveis devem aceitar:

```text
Idempotency-Key: <string>
```

Prioridade MVP:

- Create Appointment
- Reschedule Appointment
- Cancel Appointment
- Create Schedule Block
- Create Payment Order
- Request Refund

### Erros

Envelope padrão:

```json
{
  "code": "SLOT_UNAVAILABLE",
  "message": "Desculpe, este horário acabou de ser preenchido.",
  "correlationId": "f9bd5276-7438-4ba8-9a7f-df4a9daea5f0",
  "details": null
}
```

HTTP sugeridos:

```text
200 OK
201 Created
204 No Content
400 Bad Request
401 Unauthorized
403 Forbidden
404 Not Found
409 Conflict
422 Unprocessable Entity
429 Too Many Requests
500 Internal Server Error
```

---

# 2. Business Types

Suporta o onboarding com Creatable Select / Combobox.

## GET /business-types

Busca tipos existentes.

Request:

```http
GET /api/v1/business-types?search=bar&limit=20
```

Response 200:

```json
{
  "items": [
    {
      "id": "3ec6d4a0-824f-4e71-89ea-d30e286cc981",
      "name": "Barbearia",
      "slug": "barbearia",
      "isSystem": true
    }
  ]
}
```

Regras:

- retornar somente `is_active = true`;
- busca case/accent-insensitive quando possível;
- ordenar tipos oficiais/relevantes antes dos customizados.

## POST /business-types

Cria novo tipo quando o usuário não encontra uma opção adequada.

Request:

```json
{
  "name": "Studio de sobrancelhas"
}
```

Response 201:

```json
{
  "id": "63dc7a18-2c75-4a6b-bba1-c94648e34f3c",
  "name": "Studio de sobrancelhas",
  "slug": "studio-de-sobrancelhas",
  "isSystem": false
}
```

Regras:

- normalizar antes de criar;
- se já existir equivalente, retornar o tipo existente em vez de criar duplicado;
- tipo criado pelo usuário não vira automaticamente tipo oficial da plataforma.

---

# 3. Business / Onboarding

## GET /business

Retorna configuração do estabelecimento do tenant atual.

Response 200:

```json
{
  "id": "41c8725e-14d3-4d8f-89b2-d68e4472ecdd",
  "name": "Barbearia Central",
  "businessType": {
    "id": "3ec6d4a0-824f-4e71-89ea-d30e286cc981",
    "name": "Barbearia"
  },
  "timezone": "America/Sao_Paulo",
  "phone": "+5511999999999",
  "slotIntervalMinutes": 15,
  "automaticRefundOnCancellation": false,
  "refundDeadlineHoursBeforeAppointment": 24
}
```

## PUT /business

Cria/atualiza os dados principais do estabelecimento durante onboarding/configuração.

Request:

```json
{
  "name": "Barbearia Central",
  "businessTypeId": "3ec6d4a0-824f-4e71-89ea-d30e286cc981",
  "timezone": "America/Sao_Paulo",
  "phone": "+5511999999999",
  "slotIntervalMinutes": 15,
  "automaticRefundOnCancellation": false,
  "refundDeadlineHoursBeforeAppointment": 24
}
```

Response 200: representação atualizada do Business.

---

# 4. Services

## GET /services

```http
GET /api/v1/services?active=true
```

Response:

```json
{
  "items": [
    {
      "id": "c170249a-851f-4b1d-9417-75588351e00b",
      "name": "Corte",
      "description": "Corte masculino",
      "price": 50.00,
      "durationMinutes": 45,
      "requiresPayment": true,
      "isActive": true
    }
  ]
}
```

## POST /services

```json
{
  "name": "Corte + Barba",
  "description": "Corte e barba",
  "price": 80.00,
  "durationMinutes": 60,
  "requiresPayment": true
}
```

Response 201.

## PUT /services/{serviceId}

Atualiza serviço para novos agendamentos.

Importante: alteração de nome/preço/duração não muda snapshots de appointments existentes.

## DELETE /services/{serviceId}

No MVP, não realizar hard delete se houver histórico.

Comportamento recomendado:

```text
is_active = false
```

Response 204.

---

# 5. Professionals

## GET /professionals

Filtros opcionais:

```text
serviceId
active
```

## POST /professionals

```json
{
  "name": "Carlos Silva",
  "serviceIds": [
    "c170249a-851f-4b1d-9417-75588351e00b"
  ]
}
```

Response 201.

## PUT /professionals/{professionalId}

```json
{
  "name": "Carlos Silva",
  "isActive": true,
  "serviceIds": [
    "c170249a-851f-4b1d-9417-75588351e00b"
  ]
}
```

---

# 6. Availability Rules

## GET /professionals/{professionalId}/availability-rules

Response:

```json
{
  "items": [
    {
      "id": "c8ef2fc1-9456-4e07-8460-f643bb3fa079",
      "dayOfWeek": 1,
      "startTime": "09:00",
      "endTime": "12:00"
    },
    {
      "id": "a7a9161f-29de-46fb-bf40-2a3684221750",
      "dayOfWeek": 1,
      "startTime": "13:00",
      "endTime": "18:00"
    }
  ]
}
```

## PUT /professionals/{professionalId}/availability-rules

Substitui configuração recorrente do profissional de forma atômica.

Request:

```json
{
  "rules": [
    {
      "dayOfWeek": 1,
      "startTime": "09:00",
      "endTime": "12:00"
    },
    {
      "dayOfWeek": 1,
      "startTime": "13:00",
      "endTime": "18:00"
    }
  ]
}
```

Response 200.

---

# 7. Schedule Blocks

## GET /schedule-blocks

```http
GET /api/v1/schedule-blocks?from=2026-09-15T00:00:00Z&to=2026-09-16T00:00:00Z&professionalId=<uuid>
```

## POST /schedule-blocks/impact

Antes de criar um bloqueio, consulta appointments afetados.

Request:

```json
{
  "professionalId": "0a058e33-5180-4539-b52e-d47e8963acb1",
  "startsAt": "2026-09-15T17:00:00Z",
  "endsAt": "2026-09-15T21:00:00Z"
}
```

Response 200:

```json
{
  "affectedCount": 3,
  "appointments": [
    {
      "id": "0ffc7f02-a2be-4448-9b7e-8a8ba378b7fb",
      "customerName": "João",
      "serviceName": "Corte",
      "startsAt": "2026-09-15T18:00:00Z"
    }
  ]
}
```

## POST /schedule-blocks

Requer `Idempotency-Key`.

```json
{
  "professionalId": "0a058e33-5180-4539-b52e-d47e8963acb1",
  "startsAt": "2026-09-15T17:00:00Z",
  "endsAt": "2026-09-15T21:00:00Z",
  "reason": "Indisponibilidade"
}
```

Response 201.

Regra: criar ScheduleBlock não move automaticamente appointments afetados.

## DELETE /schedule-blocks/{blockId}

Response 204.

---

# 8. Customers

## GET /customers

Filtros:

```text
search
phone
page
pageSize
```

## GET /customers/{customerId}

## POST /customers

Normalmente criado automaticamente pelo fluxo WhatsApp quando necessário.

```json
{
  "name": "João da Silva",
  "phone": "+5511999999999",
  "email": null
}
```

---

# 9. Available Slots

## GET /availability/slots

Endpoint crítico do Scheduling.

Request:

```http
GET /api/v1/availability/slots?serviceId=<uuid>&date=2026-09-15&professionalId=<uuid>
```

`professionalId` é opcional.

Response 200:

```json
{
  "date": "2026-09-15",
  "timezone": "America/Sao_Paulo",
  "service": {
    "id": "c170249a-851f-4b1d-9417-75588351e00b",
    "name": "Corte",
    "durationMinutes": 45,
    "price": 50.00
  },
  "slots": [
    {
      "professionalId": "0a058e33-5180-4539-b52e-d47e8963acb1",
      "professionalName": "Carlos Silva",
      "startsAt": "2026-09-15T12:00:00Z",
      "endsAt": "2026-09-15T12:45:00Z",
      "localStartsAt": "2026-09-15T09:00:00-03:00"
    }
  ]
}
```

Regra fundamental:

> Slot retornado representa disponibilidade observada no momento da consulta. A reserva só é garantida após `POST /appointments` persistir com sucesso.

---

# 10. Appointments

## GET /appointments

Filtros:

```text
from
to
professionalId
customerId
status
page
pageSize
```

## GET /appointments/{appointmentId}

Response inclui snapshots do serviço e status de pagamento resumido.

## POST /appointments

Requer `Idempotency-Key`.

Request:

```json
{
  "customerId": "92cd2b16-bd62-467b-ad0e-d98d369284b0",
  "professionalId": "0a058e33-5180-4539-b52e-d47e8963acb1",
  "serviceId": "c170249a-851f-4b1d-9417-75588351e00b",
  "startsAt": "2026-09-15T12:00:00Z"
}
```

Response 201 quando exige pagamento:

```json
{
  "id": "0ffc7f02-a2be-4448-9b7e-8a8ba378b7fb",
  "status": "PENDING",
  "startsAt": "2026-09-15T12:00:00Z",
  "endsAt": "2026-09-15T12:45:00Z",
  "reservationExpiresAt": "2026-09-15T11:10:00Z",
  "service": {
    "name": "Corte",
    "price": 50.00,
    "durationMinutes": 45
  },
  "requiresPayment": true
}
```

### Double-booking

Se o horário foi ocupado entre consulta e tentativa de reserva:

```http
409 Conflict
```

```json
{
  "code": "SLOT_UNAVAILABLE",
  "message": "Desculpe, este horário acabou de ser preenchido. Escolha um dos horários disponíveis abaixo.",
  "correlationId": "f9bd5276-7438-4ba8-9a7f-df4a9daea5f0",
  "details": {
    "suggestedSlots": [
      {
        "professionalId": "0a058e33-5180-4539-b52e-d47e8963acb1",
        "startsAt": "2026-09-15T12:15:00Z",
        "endsAt": "2026-09-15T13:00:00Z"
      }
    ]
  }
}
```

A origem pode ser validação de aplicação ou exclusion constraint PostgreSQL. O contrato externo é o mesmo.

## POST /appointments/{appointmentId}/reschedule

Requer `Idempotency-Key`.

```json
{
  "professionalId": "0a058e33-5180-4539-b52e-d47e8963acb1",
  "startsAt": "2026-09-16T13:00:00Z"
}
```

Response 200 com appointment atualizado.

Regras:

- revalidar disponibilidade na escrita;
- manter pagamento existente quando serviço/preço não mudar;
- registrar AppointmentHistory;
- em conflito retornar `SLOT_UNAVAILABLE`.

## POST /appointments/{appointmentId}/cancel

Requer `Idempotency-Key`.

```json
{
  "reason": "Cliente solicitou cancelamento"
}
```

Response 200:

```json
{
  "id": "0ffc7f02-a2be-4448-9b7e-8a8ba378b7fb",
  "status": "CANCELLED_BY_CLIENT",
  "refund": {
    "eligible": true,
    "status": "REFUND_REQUESTED"
  }
}
```

Observação: cancellation status não espera o gateway finalizar refund.

## POST /appointments/{appointmentId}/confirm

Uso interno/autorizado após confirmação confiável de pagamento ou em fluxo sem pagamento.

Não expor ao cliente público como forma de burlar Payment Engine.

## POST /appointments/{appointmentId}/complete

Uso administrativo.

## POST /appointments/{appointmentId}/no-show

Uso administrativo.

---

# 11. Payments — Customer Service Payment

## POST /payments

Cria Payment Order para Appointment PENDING.

Requer `Idempotency-Key`.

Request:

```json
{
  "appointmentId": "0ffc7f02-a2be-4448-9b7e-8a8ba378b7fb",
  "paymentMethod": "PIX"
}
```

O valor **não** é aceito do cliente.

Backend obtém:

```text
Appointment.ServicePriceSnapshot
```

Response 201:

```json
{
  "paymentId": "8c8f63ca-77c1-4901-9e0d-e74888f939b2",
  "appointmentId": "0ffc7f02-a2be-4448-9b7e-8a8ba378b7fb",
  "amount": 50.00,
  "status": "PENDING",
  "paymentMethod": "PIX",
  "checkoutUrl": "https://provider.example/checkout/...",
  "expiresAt": "2026-09-15T11:10:00Z"
}
```

Métodos permitidos:

```text
PIX
CREDIT_CARD
DEBIT_CARD
```

Regras:

- sem sinal;
- sem pagamento parcial;
- sem boleto;
- card data nunca passa pela API da plataforma;
- hosted/tokenized checkout;
- redirect do navegador não confirma pagamento.

## GET /payments/{paymentId}

Retorna status conhecido pela plataforma.

## Webhook do provider

Exemplo conceitual:

```http
POST /api/v1/integrations/payments/{provider}/webhooks
```

Fluxo:

1. validar assinatura/autenticidade;
2. deduplicar via `webhook_inbox`;
3. responder rapidamente ao provider;
4. processar evento idempotentemente;
5. consultar API do provider quando necessário;
6. atualizar Payment;
7. emitir `PaymentConfirmed`/`PaymentFailed`;
8. Scheduling confirma Appointment somente após confirmação confiável.

---

# 12. Refunds

## POST /payments/{paymentId}/refunds

Endpoint administrativo/interno protegido.

Requer `Idempotency-Key`.

Request:

```json
{
  "reason": "Cancelamento dentro da política"
}
```

O valor não é informado pelo cliente.

Backend usa valor integral do Payment.

Response 202:

```json
{
  "refundId": "e4ec01b5-d20b-49a8-a7e2-a905dd9a38ca",
  "paymentId": "8c8f63ca-77c1-4901-9e0d-e74888f939b2",
  "amount": 50.00,
  "status": "REFUND_REQUESTED"
}
```

Nunca comunicar `REFUNDED` antes da confirmação do gateway.

## GET /refunds/{refundId}

---

# 13. Financial Analytics

## GET /analytics/financial-summary

```http
GET /api/v1/analytics/financial-summary?from=2026-09-01&to=2026-09-30
```

Response:

```json
{
  "period": {
    "from": "2026-09-01",
    "to": "2026-09-30"
  },
  "realizedRevenue": 12450.00,
  "scheduledRevenue": 3200.00,
  "averageTicket": 67.30,
  "appointments": 185,
  "cancellations": 12,
  "refunds": 7,
  "noShows": 4,
  "comparison": {
    "previousEquivalentPeriodRevenue": 11800.00,
    "percentageChange": 5.51
  }
}
```

## GET /analytics/revenue-by-professional

## GET /analytics/revenue-by-service

## GET /analytics/forecast

Analytics Engine calcula; AI Gateway apenas solicita ferramentas e apresenta resultado.

---

# 14. SaaS Billing

Customer Payments e SaaS Billing são contextos separados.

## GET /billing/subscription

## POST /billing/subscription

Request mensal:

```json
{
  "planType": "MONTHLY",
  "billingMethod": "RECURRING_CARD"
}
```

ou:

```json
{
  "planType": "MONTHLY",
  "billingMethod": "PIX"
}
```

Anual:

```json
{
  "planType": "ANNUAL_COMMITMENT",
  "billingMethod": "RECURRING_CARD"
}
```

Regra:

```text
ANNUAL_COMMITMENT + PIX -> 422 BUSINESS_RULE_VIOLATION
```

Nenhuma operação de Billing deve descontar mensalidade das vendas de Customer Payments.

---

# 15. Usage Metering

Endpoints de uso podem ser internos/admin inicialmente.

## GET /usage/summary

```http
GET /api/v1/usage/summary?from=2026-09-01&to=2026-09-30
```

Response conceitual:

```json
{
  "aiRequests": 384,
  "inputTokens": 182000,
  "outputTokens": 41000,
  "whatsappInbound": 920,
  "whatsappOutbound": 760,
  "appointments": 184,
  "estimatedAiCost": 12.31,
  "estimatedWhatsappCost": 44.80
}
```

---

# 16. WhatsApp / Conversation Integration

Endpoints externos de Meta devem ficar sob Integration Layer.

Exemplo:

```text
POST /api/v1/integrations/whatsapp/webhook
GET  /api/v1/integrations/whatsapp/webhook   (challenge, se requerido pelo provider)
```

Conversation Engine nunca chama EF Core diretamente por causa do LLM.

Fluxo:

```text
WhatsApp Adapter
   -> Messaging
   -> Conversation Engine
   -> AI Gateway
   -> Application tools
   -> Domain Modules
```

O LLM pode solicitar operações estruturadas, mas backend valida Tenant, autorização, preço, disponibilidade, estado e idempotência.

---

# 17. Códigos de erro de domínio v1

```text
VALIDATION_ERROR
UNAUTHORIZED
FORBIDDEN
RESOURCE_NOT_FOUND
BUSINESS_RULE_VIOLATION
SLOT_UNAVAILABLE
APPOINTMENT_NOT_CANCELABLE
APPOINTMENT_NOT_RESCHEDULABLE
APPOINTMENT_EXPIRED
PAYMENT_NOT_ALLOWED
PAYMENT_ALREADY_CONFIRMED
PAYMENT_PROVIDER_ERROR
REFUND_NOT_ALLOWED
REFUND_ALREADY_COMPLETED
IDEMPOTENCY_CONFLICT
RATE_LIMIT_EXCEEDED
```

Exemplo de regra inválida:

```json
{
  "code": "BUSINESS_RULE_VIOLATION",
  "message": "O profissional selecionado não executa este serviço.",
  "correlationId": "f9bd5276-7438-4ba8-9a7f-df4a9daea5f0"
}
```

---

# 18. Segurança e autorização

Perfis administrativos iniciais:

```text
OWNER
ADMIN
STAFF
```

Princípios:

- TenantId vem do contexto autenticado;
- autorização por recurso e tenant;
- webhooks usam autenticação/assinatura do provider;
- endpoints financeiros exigem permissões específicas;
- logs não armazenam dados de cartão;
- checkout é hospedado/tokenizado;
- rate limiting nos endpoints públicos/integrations;
- nunca confiar no frontend para regras financeiras.

---

# 19. Endpoints prioritários para primeira implementação

A primeira fatia vertical deve implementar apenas o necessário para provar Scheduling:

```text
GET  /business-types
POST /business-types
GET  /services
POST /services
GET  /professionals
POST /professionals
PUT  /professionals/{id}/availability-rules
GET  /availability/slots
POST /appointments
GET  /appointments/{id}
POST /appointments/{id}/reschedule
POST /appointments/{id}/cancel
```

Segunda fatia:

```text
POST /payments
GET  /payments/{id}
POST /integrations/payments/{provider}/webhooks
POST /payments/{id}/refunds
```

Terceira fatia:

```text
Schedule Blocks
WhatsApp Integration
Conversation/AI tools
Analytics
SaaS Billing
Usage Metering
```

---

# 20. Próximo passo

Após revisar e aprovar os contratos da API v1:

1. definir estrutura da solution .NET;
2. separar projetos/módulos do Modular Monolith;
3. definir referências entre camadas;
4. criar ASP.NET Core API;
5. configurar PostgreSQL + EF Core;
6. gerar primeira migration;
7. implementar a primeira fatia vertical de Scheduling;
8. adicionar testes de domínio, integração e concorrência.
