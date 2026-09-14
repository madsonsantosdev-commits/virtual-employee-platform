# ERD Físico v1 — PostgreSQL

> Status: Draft v1 — alinhado às regras aprovadas
> Data: 2026-09-14

## Convenções
PK `uuid`; instantes `timestamptz`; horários recorrentes `time`; dinheiro `numeric(12,2)`; dados tenant-owned carregam `tenant_id`. Privacy by Design: evitar duplicação de PII e exposição em logs.

## 1. tenants
```text
tenants: id PK, name, status, created_at, activated_at?, suspended_at?
```
Tenant = fronteira de propriedade, segurança e cobrança.

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
SYSTEM global; CUSTOM tenant-scoped.

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
```

## 4. legal_entities
```text
legal_entities
id uuid PK
tenant_id uuid NOT NULL
business_id uuid NOT NULL FK -> businesses.id
entity_type varchar(20) NOT NULL -- PERSON|COMPANY
document_type varchar(20) NOT NULL -- CPF|CNPJ
document_number varchar(32) NOT NULL
country_code char(2) NOT NULL DEFAULT 'BR'
legal_name varchar(180) NOT NULL
trade_name varchar(180) NULL
is_primary boolean NOT NULL DEFAULT false
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
```
Documento normalizado/validado, nunca PK, protegido contra exposição indevida.

## 5. locations
```text
locations
id uuid PK
tenant_id uuid NOT NULL
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

## 6. users
```text
users: id PK, tenant_id, email, display_name, role, status, created_at, updated_at
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
```
Service pertence ao Business. Price/Duration são defaults comerciais do Business.

## 8. location_services
```text
location_services
tenant_id uuid NOT NULL
location_id uuid NOT NULL FK -> locations.id
service_id uuid NOT NULL FK -> services.id
is_active boolean NOT NULL DEFAULT true
price_override numeric(12,2) NULL
duration_minutes_override integer NULL
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
PK(tenant_id,location_id,service_id)
CHECK(price_override IS NULL OR price_override >= 0)
CHECK(duration_minutes_override IS NULL OR duration_minutes_override > 0)
```
Define onde o Service é oferecido. Overrides ficam preparados, mas desabilitados na UX/API inicial.

## 9. service_components
```text
service_components
tenant_id uuid NOT NULL
service_id uuid NOT NULL FK -> services.id
component_service_id uuid NOT NULL FK -> services.id
created_at timestamptz NOT NULL
PK(tenant_id,service_id,component_service_id)
```
COMBO -> componentes SINGLE; sem nesting.

## 10. professionals
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
Professional pertence ao Business.

## 11. professional_locations
```text
professional_locations
tenant_id uuid NOT NULL
professional_id uuid NOT NULL FK -> professionals.id
location_id uuid NOT NULL FK -> locations.id
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
PK(tenant_id,professional_id,location_id)
```
Define onde o Professional trabalha.

## 12. professional_services
```text
professional_services
tenant_id uuid NOT NULL
professional_id uuid NOT NULL FK -> professionals.id
service_id uuid NOT NULL FK -> services.id
is_active boolean NOT NULL DEFAULT true
custom_duration_minutes integer NULL
created_at timestamptz NOT NULL
PK(tenant_id,professional_id,service_id)
```
Define capacidade, não unidade.

## 13. availability_rules
```text
availability_rules
id uuid PK
tenant_id uuid NOT NULL
location_id uuid NOT NULL FK -> locations.id
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
Agenda-base = Professional + Location.

## 14. schedule_blocks
```text
schedule_blocks
id uuid PK
tenant_id uuid NOT NULL
location_id uuid NOT NULL FK -> locations.id
professional_id uuid NULL FK -> professionals.id
starts_at timestamptz NOT NULL
ends_at timestamptz NOT NULL
reason varchar(300) NULL
created_by_user_id uuid NULL FK -> users.id
created_at timestamptz NOT NULL
CHECK(starts_at < ends_at)
```
ProfessionalId NULL bloqueia Location inteira.

## 15. customers
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
Customer permanece Business-scoped para manter histórico entre unidades.

## 16. appointments
```text
appointments
id uuid PK
tenant_id uuid NOT NULL
business_id uuid NOT NULL FK -> businesses.id
location_id uuid NOT NULL FK -> locations.id
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
```
Todo Appointment ocorre em exatamente uma Location.

## 17. appointment_items
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
UNIQUE(tenant_id,appointment_id,service_id)
```

## 18. appointment_history
Além de estados/horários/profissionais anteriores e novos, deve suportar `previous_location_id` e `new_location_id` quando houver reagendamento entre unidades.

## 19. payments
Payment integral do Appointment; sem PAN/CVV; amount == Appointment.total_price_snapshot; idempotente.

## 20. refunds
Refund integral no MVP; processo assíncrono confirmado pelo provider.

## 21. conversations
Estado conversacional tenant-scoped; conteúdo tem política própria de retenção/minimização.

## 22. subscriptions
Subscription SaaS tenant-scoped e separada de Customer Payments.

## 23. subscription_units
```text
subscription_units
subscription_id uuid NOT NULL FK -> subscriptions.id
location_id uuid NOT NULL FK -> locations.id
created_at timestamptz NOT NULL
PK(subscription_id,location_id)
```
Unidades faturáveis explícitas.

## 24. usage_records
Tenant-scoped; `location_id uuid NULL` pode atribuir consumo à unidade quando tecnicamente possível.

## 25. webhook_inbox
Deduplicação por Provider + ProviderEventId; payload com retenção definida.

## 26. outbox_messages
Eventos mínimos, preferindo IDs a PII. LocationId deve integrar payload quando necessário.

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

**LocationId não entra na constraint deliberadamente.** Um Professional não pode estar reservado simultaneamente em duas unidades.

## Elegibilidade de slot
```text
Location
  -> LocationService: todos Services disponíveis?
  -> ProfessionalLocation: Professional trabalha aqui?
  -> ProfessionalService: executa TODOS Services?
  -> AvailabilityRule(Location,Professional)
  - ScheduleBlock(Location[,Professional])
  - Appointment ativo
  = Slot elegível
```

## Relacionamentos
```text
Tenant 1 -> N Business
Business 1 -> N Location
Business 1 -> N Service
Location N -> N Service via LocationService
Business 1 -> N Professional
Professional N -> N Location via ProfessionalLocation
Professional N -> N Service via ProfessionalService
Professional + Location -> N AvailabilityRule
Location -> N ScheduleBlock
Location -> N Appointment
Customer -> N Appointment
Professional -> N Appointment
Appointment -> N AppointmentItem -> Service
Subscription N -> N Location via SubscriptionUnit
```

## Índices prioritários
```text
locations(tenant_id,business_id,is_active)
location_services(tenant_id,location_id,is_active)
location_services(tenant_id,service_id,is_active)
professional_locations(tenant_id,location_id,is_active)
professional_locations(tenant_id,professional_id,is_active)
professional_services(tenant_id,professional_id,is_active)
availability_rules(tenant_id,location_id,professional_id,day_of_week,is_active)
schedule_blocks(tenant_id,location_id,professional_id,starts_at,ends_at)
appointments(tenant_id,location_id,starts_at)
appointments(tenant_id,professional_id,starts_at)
appointments(tenant_id,customer_id,starts_at)
appointments(tenant_id,status,starts_at)
```

## Regras de integridade multi-location
- Location, Service e Professional usados juntos devem pertencer ao mesmo Tenant/Business.
- LocationService deve existir/estar ativo para cada Service reservado.
- ProfessionalLocation deve existir/estar ativo.
- ProfessionalService deve existir/estar ativo para todos os Services.
- AvailabilityRule só pode existir para ProfessionalLocation válido.
- Appointment preserva LocationId histórico mesmo se associações forem desativadas depois.
- Desativação de LocationService/ProfessionalLocation não altera Appointment histórico.

## Multi-tenancy e privacidade
TenantId do contexto autenticado, Global Query Filters EF Core, FKs/validações tenant-aware, índices tenant-scoped e testes de isolamento. RLS é evolução futura. LGPD detalhada em `docs/architecture/privacy-lgpd-v1.md`.

## Pontos antes da primeira migration
- validar constraints/FKs compostas TenantId + BusinessId entre Location/Service/Professional;
- decidir se overrides de LocationService ficam fisicamente na migration 1 ou entram somente em migration futura;
- decidir PaymentAttempt se múltiplas tentativas exigirem entidade própria;
- validar unicidade/proteção CPF/CNPJ;
- definir retenção de Conversation/webhooks/logs;
- seed oficial de BusinessTypes;
- validar combo sem nesting;
- testes automatizados de isolamento e concorrência.
