# ERD Físico v1 — PostgreSQL

> Status: Draft v1
> Data: 2026-09-11

## Objetivo

Traduzir o Modelo de Domínio v1 e as regras de Scheduling em um modelo físico inicial para PostgreSQL, preparado para implementação com EF Core.

Este documento define tabelas, chaves, relacionamentos, constraints e índices principais do MVP.

---

## Convenções gerais

- PKs: `uuid`.
- Datas absolutas: `timestamptz`.
- Horários locais recorrentes: `time`.
- Datas locais recorrentes/eventuais sem instante absoluto: `date` quando aplicável.
- Valores monetários: `numeric(12,2)`.
- Booleanos: `boolean`.
- Texto curto: `varchar(n)`.
- Texto livre: `text`.
- Enums podem iniciar como `varchar` + `CHECK`, evitando acoplamento inicial a enums físicos do PostgreSQL.
- Todas as tabelas de domínio pertencentes ao assinante carregam `tenant_id`.
- FKs devem preservar isolamento por tenant sempre que possível por meio de índices/constraints compostos.

---

## 1. tenants

```text
tenants
-------
id uuid PK
name varchar(160) NOT NULL
status varchar(30) NOT NULL
created_at timestamptz NOT NULL
activated_at timestamptz NULL
suspended_at timestamptz NULL
```

Checks sugeridos:

```text
status IN ('PENDING','ACTIVE','SUSPENDED','CANCELLED')
```

Índices:

```text
IX_tenants_status(status)
```

---

## 2. businesses

```text
businesses
----------
id uuid PK
tenant_id uuid NOT NULL FK -> tenants.id
name varchar(160) NOT NULL
business_type varchar(80) NOT NULL
timezone varchar(80) NOT NULL
phone varchar(30) NULL
address_line varchar(240) NULL
city varchar(120) NULL
state varchar(80) NULL
postal_code varchar(20) NULL
automatic_refund_on_cancellation boolean NOT NULL DEFAULT false
refund_deadline_hours_before_appointment integer NULL
slot_interval_minutes integer NOT NULL DEFAULT 15
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
```

Constraints:

```text
UNIQUE(tenant_id)
CHECK(slot_interval_minutes > 0)
CHECK(refund_deadline_hours_before_appointment IS NULL OR refund_deadline_hours_before_appointment >= 0)
```

---

## 3. users

```text
users
-----
id uuid PK
tenant_id uuid NOT NULL FK -> tenants.id
email varchar(254) NOT NULL
display_name varchar(160) NOT NULL
role varchar(40) NOT NULL
status varchar(30) NOT NULL
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
```

Constraints:

```text
UNIQUE(tenant_id, email)
role IN ('OWNER','ADMIN','STAFF')
status IN ('ACTIVE','INVITED','DISABLED')
```

---

## 4. services

```text
services
--------
id uuid PK
tenant_id uuid NOT NULL FK -> tenants.id
business_id uuid NOT NULL FK -> businesses.id
name varchar(160) NOT NULL
description text NULL
price numeric(12,2) NOT NULL
duration_minutes integer NOT NULL
requires_payment boolean NOT NULL DEFAULT true
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
```

Constraints:

```text
CHECK(price >= 0)
CHECK(duration_minutes > 0)
UNIQUE(tenant_id, id)
```

Índices:

```text
IX_services_tenant_active(tenant_id, is_active)
IX_services_business(tenant_id, business_id)
```

---

## 5. professionals

```text
professionals
-------------
id uuid PK
tenant_id uuid NOT NULL FK -> tenants.id
business_id uuid NOT NULL FK -> businesses.id
name varchar(160) NOT NULL
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
```

Índices:

```text
IX_professionals_tenant_active(tenant_id, is_active)
IX_professionals_business(tenant_id, business_id)
```

---

## 6. professional_services

```text
professional_services
---------------------
tenant_id uuid NOT NULL
professional_id uuid NOT NULL FK -> professionals.id
service_id uuid NOT NULL FK -> services.id
is_active boolean NOT NULL DEFAULT true
custom_duration_minutes integer NULL
created_at timestamptz NOT NULL

PK (tenant_id, professional_id, service_id)
```

Constraints:

```text
CHECK(custom_duration_minutes IS NULL OR custom_duration_minutes > 0)
```

---

## 7. availability_rules

```text
availability_rules
------------------
id uuid PK
tenant_id uuid NOT NULL
professional_id uuid NOT NULL FK -> professionals.id
day_of_week smallint NOT NULL
start_time time NOT NULL
end_time time NOT NULL
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
```

Constraints:

```text
CHECK(day_of_week BETWEEN 0 AND 6)
CHECK(start_time < end_time)
```

Índices:

```text
IX_availability_rules_professional_day(tenant_id, professional_id, day_of_week, is_active)
```

---

## 8. schedule_blocks

```text
schedule_blocks
---------------
id uuid PK
tenant_id uuid NOT NULL
professional_id uuid NULL FK -> professionals.id
starts_at timestamptz NOT NULL
ends_at timestamptz NOT NULL
reason varchar(300) NULL
created_by_user_id uuid NULL FK -> users.id
created_at timestamptz NOT NULL
```

Regra:

- `professional_id = NULL` significa bloqueio do estabelecimento inteiro.

Constraints:

```text
CHECK(starts_at < ends_at)
```

Índices:

```text
IX_schedule_blocks_professional_period(tenant_id, professional_id, starts_at, ends_at)
IX_schedule_blocks_tenant_period(tenant_id, starts_at, ends_at)
```

---

## 9. customers

```text
customers
---------
id uuid PK
tenant_id uuid NOT NULL
business_id uuid NOT NULL FK -> businesses.id
name varchar(160) NULL
phone varchar(30) NOT NULL
email varchar(254) NULL
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
```

Constraints:

```text
UNIQUE(tenant_id, phone)
```

Índices:

```text
IX_customers_business(tenant_id, business_id)
```

---

## 10. appointments

```text
appointments
------------
id uuid PK
tenant_id uuid NOT NULL
business_id uuid NOT NULL FK -> businesses.id
customer_id uuid NOT NULL FK -> customers.id
professional_id uuid NOT NULL FK -> professionals.id
service_id uuid NOT NULL FK -> services.id
starts_at timestamptz NOT NULL
ends_at timestamptz NOT NULL
status varchar(40) NOT NULL
reservation_expires_at timestamptz NULL
service_name_snapshot varchar(160) NOT NULL
service_price_snapshot numeric(12,2) NOT NULL
service_duration_minutes_snapshot integer NOT NULL
cancellation_reason varchar(500) NULL
cancelled_at timestamptz NULL
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
```

Status v1:

```text
PENDING
CONFIRMED
CONFIRMED_BY_CLIENT
RESCHEDULE_REQUESTED
CANCELLED_BY_CLIENT
CANCELLED_BY_BUSINESS
EXPIRED
COMPLETED
NO_SHOW
```

Constraints:

```text
CHECK(starts_at < ends_at)
CHECK(service_price_snapshot >= 0)
CHECK(service_duration_minutes_snapshot > 0)
```

### Regra de ocupação

Ocupam a agenda:

```text
PENDING
CONFIRMED
CONFIRMED_BY_CLIENT
RESCHEDULE_REQUESTED
```

Para `PENDING`, somente enquanto `reservation_expires_at > now()` em lógica de aplicação. Como partial indexes/constraints não podem depender de `now()` de forma segura como predicado mutável, a expiração deve ser materializada pela aplicação/worker mudando o status para `EXPIRED`.

### Constraint anti-double-booking

Extensão:

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;
```

Constraint conceitual:

```sql
ALTER TABLE appointments
ADD CONSTRAINT ex_appointments_no_overlap
EXCLUDE USING gist (
    tenant_id WITH =,
    professional_id WITH =,
    tstzrange(starts_at, ends_at, '[)') WITH &&
)
WHERE (
    status IN (
        'PENDING',
        'CONFIRMED',
        'CONFIRMED_BY_CLIENT',
        'RESCHEDULE_REQUESTED'
    )
);
```

Comportamento:

- primeira reserva válida vence;
- segunda tentativa concorrente recebe conflito;
- aplicação traduz para `SLOT_UNAVAILABLE`;
- mensagem ao cliente: **“Desculpe, este horário acabou de ser preenchido. Escolha um dos horários disponíveis abaixo.”**

Índices:

```text
IX_appointments_professional_start(tenant_id, professional_id, starts_at)
IX_appointments_customer_start(tenant_id, customer_id, starts_at)
IX_appointments_status_start(tenant_id, status, starts_at)
IX_appointments_business_start(tenant_id, business_id, starts_at)
```

---

## 11. appointment_history

```text
appointment_history
-------------------
id uuid PK
tenant_id uuid NOT NULL
appointment_id uuid NOT NULL FK -> appointments.id
previous_status varchar(40) NULL
new_status varchar(40) NOT NULL
previous_starts_at timestamptz NULL
new_starts_at timestamptz NULL
previous_professional_id uuid NULL
new_professional_id uuid NULL
changed_by_type varchar(30) NOT NULL
changed_by_id uuid NULL
reason varchar(500) NULL
correlation_id uuid NULL
changed_at timestamptz NOT NULL
```

Índices:

```text
IX_appointment_history_appointment(tenant_id, appointment_id, changed_at)
```

---

## 12. payments

```text
payments
--------
id uuid PK
tenant_id uuid NOT NULL
appointment_id uuid NOT NULL FK -> appointments.id
provider varchar(40) NOT NULL
provider_payment_id varchar(160) NULL
amount numeric(12,2) NOT NULL
payment_method varchar(30) NOT NULL
status varchar(30) NOT NULL
payment_purpose varchar(30) NOT NULL DEFAULT 'SERVICE'
idempotency_key varchar(160) NOT NULL
created_at timestamptz NOT NULL
confirmed_at timestamptz NULL
failed_at timestamptz NULL
```

Constraints:

```text
CHECK(amount > 0)
payment_method IN ('PIX','CREDIT_CARD','DEBIT_CARD')
status IN ('CREATED','PENDING','CONFIRMED','FAILED','CANCELLED')
UNIQUE(tenant_id, appointment_id, payment_purpose)
UNIQUE(tenant_id, idempotency_key)
```

Índices:

```text
IX_payments_appointment(tenant_id, appointment_id)
IX_payments_provider_id(provider, provider_payment_id)
IX_payments_status(tenant_id, status, created_at)
```

Regra de aplicação:

```text
Payment.amount == Appointment.service_price_snapshot
```

---

## 13. refunds

```text
refunds
-------
id uuid PK
tenant_id uuid NOT NULL
appointment_id uuid NOT NULL FK -> appointments.id
payment_id uuid NOT NULL FK -> payments.id
amount numeric(12,2) NOT NULL
reason varchar(500) NOT NULL
status varchar(40) NOT NULL
provider_refund_id varchar(160) NULL
idempotency_key varchar(160) NOT NULL
requested_at timestamptz NOT NULL
processed_at timestamptz NULL
completed_at timestamptz NULL
failed_at timestamptz NULL
```

Constraints:

```text
CHECK(amount > 0)
status IN ('REFUND_REQUESTED','REFUND_PROCESSING','REFUNDED','REFUND_FAILED')
UNIQUE(tenant_id, idempotency_key)
```

Para o MVP, apenas um refund bem-sucedido por Payment.

Sugestão de unique partial index:

```sql
CREATE UNIQUE INDEX ux_refunds_one_success_per_payment
ON refunds (tenant_id, payment_id)
WHERE status = 'REFUNDED';
```

Regra de aplicação:

```text
Refund.amount == Payment.amount
```

---

## 14. conversations

```text
conversations
-------------
id uuid PK
tenant_id uuid NOT NULL
channel varchar(30) NOT NULL
participant_type varchar(30) NOT NULL
participant_id uuid NULL
state varchar(40) NOT NULL
correlation_id uuid NULL
started_at timestamptz NOT NULL
last_interaction_at timestamptz NOT NULL
```

Checks:

```text
channel IN ('WHATSAPP','PWA')
participant_type IN ('CUSTOMER','BUSINESS_USER')
```

Índices:

```text
IX_conversations_participant(tenant_id, participant_type, participant_id, last_interaction_at)
```

---

## 15. subscriptions

```text
subscriptions
-------------
id uuid PK
tenant_id uuid NOT NULL FK -> tenants.id
provider varchar(40) NOT NULL
provider_subscription_id varchar(160) NULL
plan_type varchar(40) NOT NULL
billing_method varchar(30) NOT NULL
status varchar(40) NOT NULL
current_period_start timestamptz NULL
current_period_end timestamptz NULL
created_at timestamptz NOT NULL
cancelled_at timestamptz NULL
```

Checks:

```text
plan_type IN ('MONTHLY','ANNUAL_COMMITMENT')
billing_method IN ('RECURRING_CARD','PIX')
```

Regra comercial:

- MONTHLY: `RECURRING_CARD` ou `PIX`.
- ANNUAL_COMMITMENT: `RECURRING_CARD` apenas.

Índices:

```text
IX_subscriptions_tenant_status(tenant_id, status)
IX_subscriptions_provider(provider, provider_subscription_id)
```

---

## 16. usage_records

```text
usage_records
-------------
id uuid PK
tenant_id uuid NOT NULL
usage_type varchar(50) NOT NULL
quantity numeric(18,6) NOT NULL DEFAULT 1
estimated_cost numeric(14,6) NULL
conversation_id uuid NULL
appointment_id uuid NULL
model varchar(120) NULL
input_tokens integer NULL
output_tokens integer NULL
occurred_at timestamptz NOT NULL
```

Índices:

```text
IX_usage_records_tenant_period(tenant_id, occurred_at)
IX_usage_records_type_period(tenant_id, usage_type, occurred_at)
```

---

## 17. webhook_inbox

```text
webhook_inbox
-------------
id uuid PK
provider varchar(40) NOT NULL
provider_event_id varchar(180) NOT NULL
tenant_id uuid NULL
received_at timestamptz NOT NULL
payload_reference text NULL
status varchar(30) NOT NULL
processed_at timestamptz NULL
retry_count integer NOT NULL DEFAULT 0
last_error text NULL
correlation_id uuid NULL
```

Constraints:

```text
UNIQUE(provider, provider_event_id)
CHECK(retry_count >= 0)
```

Status sugerido:

```text
RECEIVED
PROCESSING
PROCESSED
FAILED
```

Objetivo:

- deduplicação de webhook;
- resposta HTTP rápida;
- processamento resiliente;
- auditoria financeira.

---

## 18. outbox_messages

```text
outbox_messages
---------------
id uuid PK
tenant_id uuid NULL
event_type varchar(160) NOT NULL
event_version integer NOT NULL
aggregate_id uuid NULL
correlation_id uuid NULL
causation_id uuid NULL
payload jsonb NOT NULL
occurred_at timestamptz NOT NULL
processed_at timestamptz NULL
retry_count integer NOT NULL DEFAULT 0
last_error text NULL
```

Índices:

```text
IX_outbox_unprocessed(processed_at, occurred_at)
IX_outbox_tenant(tenant_id, occurred_at)
```

Objetivo:

- persistir estado + evento na mesma transação;
- reduzir risco de perda de evento entre commit e processamento assíncrono.

---

## Relacionamentos principais

```text
Tenant 1 ─── 1 Business
Tenant 1 ─── N Users
Business 1 ─── N Services
Business 1 ─── N Professionals
Business 1 ─── N Customers
Professional N ─── N Service      via ProfessionalService
Professional 1 ─── N AvailabilityRule
Professional 1 ─── N ScheduleBlock
Customer 1 ─── N Appointment
Professional 1 ─── N Appointment
Service 1 ─── N Appointment
Appointment 1 ─── N AppointmentHistory
Appointment 1 ─── 0..1 Payment    no MVP normal
Payment 1 ─── 0..N Refund
Tenant 1 ─── N Conversation
Tenant 1 ─── N UsageRecord
Tenant 1 ─── N Subscription history
```

Observação: fisicamente `Payment` pode permitir histórico técnico de tentativas, mas o MVP parte de uma cobrança lógica por appointment/purpose. Se quisermos registrar múltiplas tentativas no mesmo Payment, isso deve ser modelado internamente ou por entidade futura `PaymentAttempt`.

---

## Diagrama textual simplificado

```text
TENANTS
  │
  ├── BUSINESSES
  │     ├── SERVICES ───────────────┐
  │     ├── PROFESSIONALS           │
  │     │      ├── AVAILABILITY     │
  │     │      ├── SCHEDULE_BLOCKS  │
  │     │      └── PROFESSIONAL_SERVICES ── SERVICES
  │     │                            │
  │     ├── CUSTOMERS                │
  │     │      │                     │
  │     │      └──── APPOINTMENTS ───┘
  │     │                 │
  │     │                 ├── APPOINTMENT_HISTORY
  │     │                 └── PAYMENTS
  │     │                        └── REFUNDS
  │     │
  │     └── USERS
  │
  ├── SUBSCRIPTIONS
  ├── CONVERSATIONS
  ├── USAGE_RECORDS
  ├── WEBHOOK_INBOX
  └── OUTBOX_MESSAGES
```

---

## Constraints multi-tenant

Somente possuir `tenant_id` não garante isolamento completo.

Na implementação EF Core/PostgreSQL devemos combinar:

1. Query Filter global por `TenantId` onde apropriado.
2. `TenantId` derivado do contexto autenticado, nunca aceito cegamente do cliente.
3. Índices iniciando por `tenant_id` em consultas tenant-scoped relevantes.
4. Validação de FKs lógicas para impedir associação entre entidades de tenants diferentes.
5. Testes automatizados específicos de isolamento.

Evolução futura possível: Row-Level Security no PostgreSQL, se justificar complexidade adicional.

---

## Decisões que ficam para implementação EF Core

1. Naming convention `snake_case` no PostgreSQL.
2. Configuração de `uuid` e geração de IDs na aplicação.
3. Conversão de enums de domínio para strings.
4. Global Query Filters por tenant.
5. Interceptor/auditoria de `CreatedAt`/`UpdatedAt`.
6. Migration manual para `btree_gist` e `EXCLUDE CONSTRAINT`, se o provider EF Core não expressar toda a constraint de forma conveniente.
7. Tratamento de SQLSTATE da exclusion violation para converter em `SLOT_UNAVAILABLE`.

---

## Pontos de revisão antes da primeira migration

Antes de gerar a migration inicial, revisar:

- se Customer por telefone deve ser único por tenant ou por business;
- se haverá mais de um Business por Tenant no futuro;
- se `Payment` deve ser 1:1 lógico ou suportar `PaymentAttempt` desde o início;
- se `ProfessionalService.custom_duration_minutes` entra no MVP;
- retenção de conversations e payloads de webhook;
- política de PII e LGPD para dados de cliente;
- estratégia exata de roles/permissões.

---

## Próximo passo

Após revisão deste ERD físico v1:

1. fechar decisões pendentes;
2. criar contratos de API v1;
3. definir estrutura física da solution .NET;
4. criar projetos e dependências;
5. iniciar migrations e implementação do Scheduling Core.
