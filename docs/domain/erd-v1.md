# ERD Físico v1 — PostgreSQL

> Status: Draft v1 — alinhado às regras aprovadas
> Data: 2026-09-14

## Convenções

PK `uuid`; instantes `timestamptz`; horários recorrentes `time`; dinheiro `numeric(12,2)`; enums inicialmente `varchar + CHECK`; dados tenant-owned carregam `tenant_id`.

## 1. tenants
```text
tenants
id uuid PK
name varchar(160) NOT NULL
status varchar(30) NOT NULL
created_at timestamptz NOT NULL
activated_at timestamptz NULL
suspended_at timestamptz NULL
CHECK status: PENDING|ACTIVE|SUSPENDED|CANCELLED
```

Tenant é a fronteira de propriedade, segurança e cobrança. Não representa obrigatoriamente uma unidade física.

## 2. business_types
```text
business_types
id uuid PK
tenant_id uuid NULL FK -> tenants.id
name varchar(80) NOT NULL
normalized_name varchar(100) NOT NULL
slug varchar(100) NOT NULL
is_system boolean NOT NULL DEFAULT false
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
```

Regras:
- `tenant_id = NULL`: tipo oficial/system da plataforma;
- `tenant_id != NULL`: tipo customizado pertencente ao tenant;
- remover `created_by_tenant_id`, pois `tenant_id` define o escopo/proprietário do tipo customizado;
- unicidade de `normalized_name`/`slug` deve respeitar o escopo: global entre tipos SYSTEM e por tenant entre tipos CUSTOM, implementada por índices únicos parciais/escopados na migration.

## 3. businesses
```text
businesses
id uuid PK
tenant_id uuid NOT NULL FK -> tenants.id
business_type_id uuid NOT NULL FK -> business_types.id
name varchar(160) NOT NULL
automatic_refund_on_cancellation boolean NOT NULL DEFAULT false
refund_deadline_hours_before_appointment integer NULL
slot_interval_minutes integer NOT NULL DEFAULT 15
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
UNIQUE(tenant_id)
CHECK(slot_interval_minutes > 0)
```

No MVP a experiência inicial continua 1 Tenant -> 1 Business. Endereço/timezone operacional deixam Business e pertencem à Location.

## 4. legal_entities
```text
legal_entities
id uuid PK
tenant_id uuid NOT NULL FK -> tenants.id
business_id uuid NOT NULL FK -> businesses.id
entity_type varchar(20) NOT NULL
document_type varchar(20) NOT NULL
document_number varchar(32) NOT NULL
country_code char(2) NOT NULL DEFAULT 'BR'
legal_name varchar(180) NOT NULL
trade_name varchar(180) NULL
is_primary boolean NOT NULL DEFAULT false
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
CHECK(entity_type IN ('PERSON','COMPANY'))
CHECK(document_type IN ('CPF','CNPJ'))
```

Regras: documento normalizado sem formatação; CPF/CNPJ validados no backend; documento não é PK; não registrar documento completo em logs; alterações relevantes devem ser auditáveis. A unicidade fiscal será validada na migration considerando `country_code + document_type + document_number` e regras de isolamento.

## 5. locations
```text
locations
id uuid PK
tenant_id uuid NOT NULL FK -> tenants.id
business_id uuid NOT NULL FK -> businesses.id
legal_entity_id uuid NULL FK -> legal_entities.id
name varchar(160) NOT NULL
phone varchar(30) NULL
address_line1 varchar(180) NULL
address_line2 varchar(180) NULL
number varchar(30) NULL
district varchar(120) NULL
city varchar(120) NULL
state varchar(80) NULL
postal_code varchar(20) NULL
country_code char(2) NOT NULL DEFAULT 'BR'
timezone varchar(80) NOT NULL
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
```

Location é a unidade operacional física ou virtual. O modelo suporta 1 Business -> N Locations, embora o MVP inicialmente crie apenas uma unidade por negócio. Uma Location pode referenciar a entidade legal responsável por aquela unidade.

## 6. users
```text
users
id uuid PK
tenant_id uuid NOT NULL
email varchar(254) NOT NULL
display_name varchar(160) NOT NULL
role varchar(40) NOT NULL
status varchar(30) NOT NULL
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
UNIQUE(tenant_id,email)
```

## 7. services
```text
services
id uuid PK
tenant_id uuid NOT NULL
business_id uuid NOT NULL FK -> businesses.id
name varchar(160) NOT NULL
description text NULL
service_type varchar(20) NOT NULL DEFAULT 'SINGLE'
price numeric(12,2) NOT NULL
duration_minutes integer NOT NULL
requires_payment boolean NOT NULL DEFAULT true
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
CHECK(service_type IN ('SINGLE','COMBO'))
CHECK(price >= 0)
CHECK(duration_minutes > 0)
UNIQUE(tenant_id,id)
```

Combo possui preço/duração próprios. O escopo Business x Location será fechado na próxima revisão do catálogo.

## 8. service_components
```text
service_components
tenant_id uuid NOT NULL
service_id uuid NOT NULL FK -> services.id
component_service_id uuid NOT NULL FK -> services.id
created_at timestamptz NOT NULL
PK(tenant_id,service_id,component_service_id)
CHECK(service_id <> component_service_id)
```

Regras de aplicação: `service_id` deve ser COMBO; componente deve ser SINGLE; combos aninhados não entram no MVP; componentes não determinam preço/duração do combo.

## 9. professionals
```text
professionals
id uuid PK
tenant_id uuid NOT NULL
business_id uuid NOT NULL FK -> businesses.id
name varchar(160) NOT NULL
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
```

## 10. professional_services
```text
professional_services
tenant_id uuid NOT NULL
professional_id uuid NOT NULL FK -> professionals.id
service_id uuid NOT NULL FK -> services.id
is_active boolean NOT NULL DEFAULT true
custom_duration_minutes integer NULL
created_at timestamptz NOT NULL
PK(tenant_id,professional_id,service_id)
CHECK(custom_duration_minutes IS NULL OR custom_duration_minutes > 0)
```

`ProfessionalService` é a matriz de capacidade. Para Appointment com N serviços, o profissional deve estar habilitado para todos. `custom_duration_minutes` fica preparado, mas não é usado no MVP inicial.

## 11. availability_rules
```text
availability_rules
id uuid PK
tenant_id uuid NOT NULL
professional_id uuid NOT NULL FK -> professionals.id
day_of_week smallint NOT NULL
start_time time NOT NULL
end_time time NOT NULL
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
CHECK(day_of_week BETWEEN 0 AND 6)
CHECK(start_time < end_time)
```

Agenda recorrente é reaproveitável pela aplicação entre ciclos; não materializar slots/semanas no banco.

## 12. schedule_blocks
```text
schedule_blocks
id uuid PK
tenant_id uuid NOT NULL
professional_id uuid NULL FK -> professionals.id
starts_at timestamptz NOT NULL
ends_at timestamptz NOT NULL
reason varchar(300) NULL
created_by_user_id uuid NULL FK -> users.id
created_at timestamptz NOT NULL
CHECK(starts_at < ends_at)
```

`professional_id = NULL` significa bloqueio do estabelecimento/unidade conforme escopo operacional a fechar na revisão de Scheduling multi-location.

## 13. customers
```text
customers
id uuid PK
tenant_id uuid NOT NULL
business_id uuid NOT NULL FK -> businesses.id
name varchar(160) NULL
phone varchar(30) NOT NULL
email varchar(254) NULL
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
UNIQUE(tenant_id,phone)
```

## 14. appointments
```text
appointments
id uuid PK
tenant_id uuid NOT NULL
business_id uuid NOT NULL FK -> businesses.id
customer_id uuid NOT NULL FK -> customers.id
professional_id uuid NOT NULL FK -> professionals.id
starts_at timestamptz NOT NULL
ends_at timestamptz NOT NULL
total_price_snapshot numeric(12,2) NOT NULL
total_duration_minutes_snapshot integer NOT NULL
status varchar(40) NOT NULL
reservation_expires_at timestamptz NULL
cancellation_reason varchar(500) NULL
cancelled_at timestamptz NULL
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
CHECK(starts_at < ends_at)
CHECK(total_price_snapshot >= 0)
CHECK(total_duration_minutes_snapshot > 0)
```

Status: `PENDING`, `CONFIRMED`, `CONFIRMED_BY_CLIENT`, `RESCHEDULE_REQUESTED`, `CANCELLED_BY_CLIENT`, `CANCELLED_BY_BUSINESS`, `EXPIRED`, `COMPLETED`, `NO_SHOW`.

Um Appointment tem um único Professional no MVP. `location_id` será incorporado quando fecharmos o escopo operacional multi-location no bloco Scheduling.

## 15. appointment_items
```text
appointment_items
id uuid PK
tenant_id uuid NOT NULL
appointment_id uuid NOT NULL FK -> appointments.id
service_id uuid NOT NULL FK -> services.id
service_name_snapshot varchar(160) NOT NULL
price_snapshot numeric(12,2) NOT NULL
duration_minutes_snapshot integer NOT NULL
created_at timestamptz NOT NULL
CHECK(price_snapshot >= 0)
CHECK(duration_minutes_snapshot > 0)
UNIQUE(tenant_id,appointment_id,service_id)
```

Sem `quantity` no MVP. Cada serviço/combo aparece no máximo uma vez no Appointment.

Regras: soma dos preços = total do Appointment; soma das durações = duração total; `ends_at = starts_at + total duration`. Validadas pelo domínio/transação.

## 16. appointment_history
```text
appointment_history
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

## 17. payments
```text
payments
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
CHECK(amount > 0)
UNIQUE(tenant_id,appointment_id,payment_purpose)
UNIQUE(tenant_id,idempotency_key)
```

Regra: `Payment.amount == Appointment.total_price_snapshot`.

## 18. refunds
```text
refunds
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
Regra MVP: `Refund.amount == Payment.amount`; refund integral apenas.

## 19. conversations
```text
conversations
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

## 20. subscriptions
```text
subscriptions
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

## 21. subscription_units
```text
subscription_units
subscription_id uuid NOT NULL FK -> subscriptions.id
location_id uuid NOT NULL FK -> locations.id
created_at timestamptz NOT NULL
PK(subscription_id,location_id)
```

A cobrança possui unidades faturáveis explícitas. Não inferir que uma Subscription cobre automaticamente todas as Locations do Tenant. No MVP haverá uma Location inicial; o modelo já evita cobrança única acidental de múltiplas unidades futuras.

## 22. usage_records
```text
usage_records
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

## 23. webhook_inbox
```text
webhook_inbox
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
UNIQUE(provider,provider_event_id)
```

## 24. outbox_messages
```text
outbox_messages
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

## Anti-double-booking

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;
ALTER TABLE appointments
ADD CONSTRAINT ex_appointments_no_overlap
EXCLUDE USING gist (
  tenant_id WITH =,
  professional_id WITH =,
  tstzrange(starts_at, ends_at, '[)') WITH &&
)
WHERE (status IN ('PENDING','CONFIRMED','CONFIRMED_BY_CLIENT','RESCHEDULE_REQUESTED'));
```

PENDING expirado deve ser materializado como `EXPIRED` por worker; não usar `now()` no predicado da constraint.

## Relacionamentos

```text
Tenant 1 -> N Business
Business N -> 1 BusinessType
Business 1 -> N LegalEntity
Business 1 -> N Location
Location N -> 0..1 LegalEntity
Subscription N -> N Location via SubscriptionUnit
Business 1 -> N Services
Service(COMBO) N -> N Service(SINGLE) via ServiceComponent
Professional N -> N Service via ProfessionalService
Professional 1 -> N AvailabilityRule
Professional 1 -> N ScheduleBlock
Customer 1 -> N Appointment
Professional 1 -> N Appointment
Appointment 1 -> N AppointmentItem
Service 1 -> N AppointmentItem
Appointment 1 -> N AppointmentHistory
Appointment 1 -> 0..1 Payment lógico MVP
Payment 1 -> 0..N Refund
```

## Índices prioritários

```text
business_types(tenant_id,normalized_name)
legal_entities(tenant_id,business_id)
locations(tenant_id,business_id,is_active)
services(tenant_id,is_active)
professionals(tenant_id,is_active)
availability_rules(tenant_id,professional_id,day_of_week,is_active)
schedule_blocks(tenant_id,professional_id,starts_at,ends_at)
appointments(tenant_id,professional_id,starts_at)
appointments(tenant_id,customer_id,starts_at)
appointments(tenant_id,status,starts_at)
appointment_items(tenant_id,appointment_id)
payments(tenant_id,appointment_id)
subscription_units(subscription_id,location_id)
usage_records(tenant_id,occurred_at)
```

## Multi-tenancy

Combinar TenantId do contexto autenticado, Global Query Filters EF Core, FKs/validações tenant-aware, índices tenant-scoped e testes de isolamento. RLS fica como evolução futura.

## Pontos antes da primeira migration

- fechar escopo Business x Location de Services e Professionals;
- incorporar `location_id` ao Scheduling onde operacionalmente necessário;
- decidir PaymentAttempt se múltiplas tentativas precisarem de entidade própria;
- manter `ProfessionalService.custom_duration_minutes` desabilitado na UX inicial;
- validar constraints compostas de TenantId;
- validar índices únicos parciais de `business_types` SYSTEM/CUSTOM;
- validar unicidade e proteção de CPF/CNPJ;
- retenção/LGPD de Conversation/webhooks;
- seed oficial de business types;
- validar regra de combo sem nesting.
