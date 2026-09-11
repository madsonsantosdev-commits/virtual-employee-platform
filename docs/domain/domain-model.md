# Modelo de Domínio v1

> Status: Draft v1
> Data: 2026-09-11

## Objetivo

Definir o modelo de domínio inicial da Virtual Employee Platform antes da implementação física da solução .NET e do banco PostgreSQL.

Este documento é propositalmente orientado a domínio, não a tabelas. O ERD físico poderá evoluir sem quebrar as invariantes descritas aqui.

## Princípios

1. **Multi-Tenant desde o primeiro dia**: toda entidade pertencente ao negócio deve ser isolada por `TenantId`.
2. **Scheduling é o domínio operacional central**.
3. **IA interpreta; backend valida; domínio executa**.
4. **Pagamento nunca é confirmado por redirect de browser**; apenas por webhook/API validada do gateway.
5. **Preço histórico não pode mudar retroativamente**.
6. **Agendamentos não são apagados fisicamente**; mudanças de estado precisam ser auditáveis.
7. **Cancelamento operacional e estorno financeiro são processos separados**.
8. **Sem sinal, pagamento parcial ou estorno parcial no MVP**.
9. **Evitar double-booking por regra transacional e constraint de banco**.

---

## Agregados e entidades principais

### Tenant

Representa o assinante lógico da plataforma e é a fronteira de isolamento de dados.

Campos conceituais:

- `TenantId`
- `Name`
- `Status`
- `CreatedAt`
- `ActivatedAt`
- `SuspendedAt`

Relacionamentos:

- 1 Tenant -> 1 Business
- 1 Tenant -> N Users
- 1 Tenant -> N Services
- 1 Tenant -> N Professionals
- 1 Tenant -> N Customers
- 1 Tenant -> N Appointments
- 1 Tenant -> N Conversations
- 1 Tenant -> N UsageRecords
- 1 Tenant -> 1..N SubscriptionPeriods/Subscription records ao longo do tempo

### Business

Representa o estabelecimento configurado pelo assinante.

Campos conceituais:

- `BusinessId`
- `TenantId`
- `Name`
- `BusinessType`
- `Timezone`
- `Phone`
- `Address`
- `CancellationPolicyId`
- `AutomaticRefundOnCancellation`
- `RefundDeadlineHoursBeforeAppointment`
- `CreatedAt`
- `UpdatedAt`

### Service

Catálogo de serviços oferecidos pelo estabelecimento.

Campos conceituais:

- `ServiceId`
- `TenantId`
- `BusinessId`
- `Name`
- `Description`
- `Price`
- `DurationMinutes`
- `IsActive`
- `RequiresPayment`
- `CreatedAt`
- `UpdatedAt`

Regras:

- `Price > 0` quando pagamento for obrigatório.
- `DurationMinutes > 0`.
- Alterar o preço do Service não pode alterar appointments históricos.

### Professional

Profissional que executa um ou mais serviços.

Campos conceituais:

- `ProfessionalId`
- `TenantId`
- `BusinessId`
- `Name`
- `IsActive`
- `CreatedAt`
- `UpdatedAt`

Relacionamento N:N com Service por `ProfessionalService`.

### ProfessionalService

Define quais serviços cada profissional pode executar.

Campos conceituais:

- `TenantId`
- `ProfessionalId`
- `ServiceId`
- `IsActive`
- `CustomDurationMinutes` (opcional, futuro)

MVP: usar `Service.DurationMinutes` por padrão.

### AvailabilityRule

Representa disponibilidade recorrente planejada do profissional.

Exemplo: segunda-feira das 09:00 às 18:00.

Campos conceituais:

- `AvailabilityRuleId`
- `TenantId`
- `ProfessionalId`
- `DayOfWeek`
- `StartTime`
- `EndTime`
- `IsActive`

### ScheduleBlock

Representa indisponibilidade excepcional.

Exemplos:

- almoço;
- férias;
- médico;
- estabelecimento fechado;
- “não vou trabalhar amanhã à tarde”.

Campos conceituais:

- `ScheduleBlockId`
- `TenantId`
- `ProfessionalId` nullable quando o bloqueio valer para o estabelecimento inteiro
- `StartsAt`
- `EndsAt`
- `Reason`
- `CreatedByUserId`
- `CreatedAt`

Regra:

- um bloqueio novo deve permitir localizar appointments afetados antes de qualquer realocação.
- appointments não são movidos automaticamente sem consentimento do cliente.

### Customer

Cliente final atendido pelo estabelecimento.

Campos conceituais:

- `CustomerId`
- `TenantId`
- `BusinessId`
- `Name`
- `Phone`
- `Email` opcional
- `CreatedAt`
- `UpdatedAt`

MVP: telefone/WhatsApp é o identificador operacional principal, mas não deve ser usado como chave primária.

### Appointment

Principal agregado operacional do Scheduling.

Campos conceituais:

- `AppointmentId`
- `TenantId`
- `BusinessId`
- `CustomerId`
- `ProfessionalId`
- `ServiceId`
- `StartsAt`
- `EndsAt`
- `Status`
- `CreatedAt`
- `UpdatedAt`
- `CancelledAt`
- `CancellationReason`

#### Snapshot obrigatório do serviço

O Appointment deve armazenar um snapshot mínimo do serviço no momento da criação:

- `ServiceNameSnapshot`
- `ServicePriceSnapshot`
- `ServiceDurationMinutesSnapshot`

Motivo: se o estabelecimento alterar posteriormente preço, nome ou duração do Service, o appointment histórico deve preservar o contexto comercial original.

#### Status v1

- `PENDING`
- `CONFIRMED`
- `CONFIRMED_BY_CLIENT`
- `RESCHEDULE_REQUESTED`
- `RESCHEDULED`
- `CANCELLED_BY_CLIENT`
- `CANCELLED_BY_BUSINESS`
- `COMPLETED`
- `NO_SHOW`

#### Invariantes principais

1. `StartsAt < EndsAt`.
2. `EndsAt - StartsAt` deve refletir a duração acordada no snapshot.
3. O Professional precisa estar habilitado para o Service.
4. O horário precisa respeitar disponibilidade e bloqueios.
5. Não pode haver sobreposição de appointments ativos para o mesmo profissional.
6. Appointment pago só é `CONFIRMED` após confirmação financeira válida.
7. Reagendamento precisa revalidar disponibilidade no momento da gravação.

### AppointmentHistory

Registra mudanças importantes do appointment.

Campos conceituais:

- `AppointmentHistoryId`
- `TenantId`
- `AppointmentId`
- `PreviousStatus`
- `NewStatus`
- `ChangedAt`
- `ChangedByType` (`Customer`, `BusinessUser`, `System`, `Webhook`)
- `ChangedById` opcional
- `Reason`
- `CorrelationId`

Uso:

- auditoria;
- troubleshooting;
- histórico de reagendamento/cancelamento;
- analytics operacional.

### Payment

Representa o pagamento integral do serviço.

Campos conceituais:

- `PaymentId`
- `TenantId`
- `AppointmentId`
- `Provider`
- `ProviderPaymentId`
- `Amount`
- `PaymentMethod` (`PIX`, `CREDIT_CARD`, `DEBIT_CARD`)
- `Status`
- `IdempotencyKey`
- `CreatedAt`
- `ConfirmedAt`
- `FailedAt`

Status v1:

- `CREATED`
- `PENDING`
- `CONFIRMED`
- `FAILED`
- `CANCELLED`

Invariantes:

- `Amount == Appointment.ServicePriceSnapshot`.
- MVP permite apenas pagamento integral.
- Pagamento confirmado somente por webhook/API confiável do provider.
- `TenantId + AppointmentId + PaymentPurpose` deve ser idempotente logicamente.

### Refund

Representa o processo financeiro de estorno total.

Campos conceituais:

- `RefundId`
- `TenantId`
- `AppointmentId`
- `PaymentId`
- `Amount`
- `Reason`
- `Status`
- `ProviderRefundId`
- `IdempotencyKey`
- `RequestedAt`
- `ProcessedAt`
- `CompletedAt`
- `FailedAt`

Status v1:

- `REFUND_REQUESTED`
- `REFUND_PROCESSING`
- `REFUNDED`
- `REFUND_FAILED`

Invariantes:

- `Amount == Payment.Amount` no MVP.
- apenas um refund financeiro bem-sucedido por Payment.
- nunca comunicar “estornado” antes da confirmação do gateway.

### Conversation

Contexto mínimo de uma conversa com cliente ou estabelecimento.

Campos conceituais:

- `ConversationId`
- `TenantId`
- `Channel`
- `ParticipantType`
- `ParticipantId` opcional
- `State`
- `StartedAt`
- `LastInteractionAt`
- `CorrelationId`

Estados conversacionais possíveis para fluxo de booking:

- `NEW`
- `SERVICE_SELECTED`
- `DATE_REQUESTED`
- `SLOT_SELECTED`
- `AWAITING_CONFIRMATION`
- `AWAITING_PAYMENT`
- `BOOKED`

Observação: estado conversacional não substitui estado do Appointment.

### Subscription

Representa a assinatura SaaS do estabelecimento, separada dos pagamentos de serviços dos clientes finais.

Campos conceituais:

- `SubscriptionId`
- `TenantId`
- `Provider`
- `ProviderSubscriptionId`
- `PlanType` (`MONTHLY`, `ANNUAL_COMMITMENT`)
- `BillingMethod`
- `Status`
- `CurrentPeriodStart`
- `CurrentPeriodEnd`
- `CreatedAt`
- `CancelledAt`

Regras comerciais atuais:

- mensal: cartão recorrente ou Pix;
- anual/12 meses: cartão recorrente obrigatório;
- sem boleto;
- Billing não movimenta Customer Payments.

### UsageRecord

Metering de custo e consumo por tenant.

Campos conceituais:

- `UsageRecordId`
- `TenantId`
- `UsageType`
- `Quantity`
- `EstimatedCost`
- `ConversationId` opcional
- `AppointmentId` opcional
- `Model` opcional
- `InputTokens` opcional
- `OutputTokens` opcional
- `OccurredAt`

Tipos esperados:

- AI request
- WhatsApp inbound
- WhatsApp outbound
- appointment
- payment
- refund

---

## Relacionamentos conceituais

```text
Tenant
 └── Business
      ├── Services
      ├── Professionals
      │    ├── ProfessionalServices
      │    ├── AvailabilityRules
      │    └── ScheduleBlocks
      ├── Customers
      └── Appointments
           ├── AppointmentHistory
           ├── Payment
           │    └── Refund
           └── Conversation (associação contextual, quando aplicável)

Tenant
 ├── Users
 ├── Subscription
 ├── Conversations
 ├── UsageRecords
 └── Audit/Observability
```

---

## Decisões importantes do ERD v1

### 1. Snapshot do Service dentro de Appointment

**Decisão:** sim.

Guardar nome, preço e duração acordados no momento da reserva evita inconsistência histórica e financeira.

### 2. AvailabilityRule e ScheduleBlock separados

**Decisão:** sim.

- `AvailabilityRule` = agenda recorrente planejada.
- `ScheduleBlock` = exceção/indisponibilidade específica.

Isso evita transformar disponibilidade em milhares de slots materializados prematuramente.

### 3. Não persistir Slot como entidade principal no MVP

**Decisão:** calcular slots disponíveis a partir de:

`AvailabilityRule - ScheduleBlock - Appointments ativos`

Um slot é inicialmente um resultado calculado, não uma linha de banco permanente.

### 4. AppointmentHistory separado

**Decisão:** sim.

Appointment mantém estado atual; AppointmentHistory registra transições relevantes.

### 5. Payment separado de Appointment

**Decisão:** sim.

O ciclo financeiro tem estados e idempotência próprios e não deve poluir o agregado de Scheduling.

### 6. Refund separado de Payment

**Decisão:** sim.

Cancelamento operacional pode ocorrer antes ou independentemente da conclusão do refund.

### 7. Billing separado de Customer Payments

**Decisão:** obrigatória.

São duas relações financeiras diferentes e não devem compartilhar regras de domínio.

---

## Concorrência e double-booking

Consultar disponibilidade não garante a vaga.

Fluxo correto:

1. cliente consulta slots;
2. sistema exibe horário aparentemente disponível;
3. cliente escolhe;
4. `CreateAppointment` revalida disponibilidade;
5. transação/constraint impede conflito;
6. somente um concorrente consegue reservar;
7. outro recebe novos horários.

O desenho físico no PostgreSQL deve incluir uma estratégia de proteção contra sobreposição para appointments considerados ativos.

---

## Próximas decisões antes do modelo físico

1. Definir timezone e armazenamento temporal (`timestamptz` em UTC + Business.Timezone).
2. Definir constraint PostgreSQL para impedir sobreposição de appointments ativos.
3. Definir estratégia de soft-delete apenas onde realmente necessário; Appointment não terá delete operacional.
4. Definir índices Tenant-aware.
5. Definir tamanho e retenção de Conversation/Message histórico.
6. Definir modelo exato de User/Role/Permission.
7. Detalhar WebhookInbox e OutboxMessage no modelo de infraestrutura.
8. Transformar este modelo em ERD físico v1.

---

## Regra central

> **IA interpreta → Backend valida → Domínio executa.**

Para operações financeiras:

> **IA interpreta → Usuário autoriza quando necessário → Backend valida → Domínio executa → Gateway processa → Webhook confirma.**
