# ERD Físico v1 — PostgreSQL

> Status: Draft v1 — alinhado às regras aprovadas
> Data: 2026-09-14

## Convenções
PK `uuid`; instantes `timestamptz`; horários recorrentes `time`; dinheiro `numeric(12,2)`; dados tenant-owned carregam `tenant_id`. Privacy by Design: evitar duplicação de PII e exposição em logs.

## Entidades principais
- Tenant = fronteira de propriedade, segurança e cobrança.
- Business = negócio/marca.
- LegalEntity = identidade fiscal.
- Location = unidade operacional.
- Service e Professional pertencem ao Business.
- Customer permanece Business-scoped.
- Appointment sempre ocorre em uma Location e contém 1..N AppointmentItems.

## Multi-location
```text
Tenant
  -> Business
       -> Locations
       -> Services -> LocationService
       -> Professionals -> ProfessionalLocation
                        -> ProfessionalService
```

`AvailabilityRule` é Professional + Location. `ScheduleBlock` sempre é Location-scoped e opcionalmente Professional-scoped.

## LocationService — APROVADO PARA MIGRATION 001
```text
location_services
tenant_id uuid NOT NULL
location_id uuid NOT NULL FK -> locations.id
service_id uuid NOT NULL FK -> services.id
is_active boolean NOT NULL DEFAULT true
created_at timestamptz NOT NULL
updated_at timestamptz NOT NULL
PK(tenant_id,location_id,service_id)
```

`LocationService` define somente se um Service do Business está disponível em determinada Location.

**Migration 001 não terá `price_override` nem `duration_minutes_override`.** Preço e duração permanecem definidos em `Service` no MVP. Caso surja requisito real de preço/duração por unidade, a evolução será feita por migration própria, com impacto explícito em API, snapshots, analytics e regras de booking.

Motivação: reduzir complexidade, joins condicionais e ambiguidade comercial no hot path do Scheduling sem antecipar um requisito ainda não comprovado.

## Appointment
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

## Payment
`Payment` representa a obrigação financeira lógica do Appointment.

```text
payments
id uuid PK
tenant_id uuid NOT NULL
appointment_id uuid NOT NULL FK -> appointments.id
amount numeric(12,2) NOT NULL
status varchar(30) NOT NULL
payment_purpose varchar(30) NOT NULL DEFAULT 'SERVICE'
created_at timestamptz NOT NULL
confirmed_at timestamptz NULL
cancelled_at timestamptz NULL
updated_at timestamptz NOT NULL
CHECK(amount > 0)
UNIQUE(tenant_id,appointment_id,payment_purpose)
```

Para SERVICE no MVP, `Payment.Amount == Appointment.TotalPriceSnapshot`.

## PaymentAttempt
`PaymentAttempt` representa cada tentativa concreta de cobrança no provider.

```text
payment_attempts
id uuid PK
tenant_id uuid NOT NULL
payment_id uuid NOT NULL FK -> payments.id
provider varchar(40) NOT NULL
provider_payment_id varchar(160) NULL
payment_method varchar(30) NOT NULL
status varchar(30) NOT NULL
idempotency_key varchar(160) NOT NULL
checkout_reference varchar(500) NULL
expires_at timestamptz NULL
created_at timestamptz NOT NULL
processed_at timestamptz NULL
confirmed_at timestamptz NULL
failed_at timestamptz NULL
failure_code varchar(100) NULL
failure_message varchar(500) NULL
UNIQUE(tenant_id,idempotency_key)
```

Índice único parcial `(provider, provider_payment_id)` quando `provider_payment_id IS NOT NULL`.

Uma tentativa FAILED/EXPIRED não cria nova obrigação: Payment pode continuar PENDING e receber novo PaymentAttempt enquanto Appointment for reservável.

## Refund
Refund integral no MVP ligado ao Payment e ao PaymentAttempt confirmado que originou a transação externa. Estado final somente após confirmação do provider.

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

`LocationId` deliberadamente não entra: Professional não pode estar reservado simultaneamente em duas unidades.

## Elegibilidade / hot path do Scheduling
```text
Tenant + Location
  -> LocationService
  -> ProfessionalLocation
  -> ProfessionalService
  -> AvailabilityRule(Location,Professional)
  - ScheduleBlock(Location[,Professional])
  - Appointment ativo
  = Slot elegível
```

Preço/duração são obtidos diretamente de `Service` no MVP, sem fallback/override por Location.

## Estratégia de integridade Multi-Tenant — APROVADA

A arquitetura adota isolamento estrutural por `TenantId` com foco simultâneo em segurança, simplicidade e performance.

### Banco
- `tenant_id` obrigatório em dados tenant-owned;
- consultas sempre partem do TenantContext;
- relacionamentos críticos são tenant-aware;
- índices são tenant-scoped quando compatíveis com o padrão de consulta;
- PostgreSQL permanece a última barreira de isolamento crítico.

### Domínio / aplicação
Coerência operacional específica entre Business, Location, Service e Professional é validada no domínio/aplicação. Não adicionar `BusinessId` indiscriminadamente a todas as FKs compostas apenas por redundância.

### Regra arquitetural
> **TenantId é a principal barreira estrutural no banco. Regras específicas de Business pertencem ao domínio. Constraints e índices adicionais entram quando entregarem benefício real de integridade, concorrência ou performance.**

### Motivação de performance
Evitar FKs/índices compostos desnecessariamente largos reduz:
- tamanho de índices;
- custo de INSERT/UPDATE;
- pressão de memória/cache;
- complexidade de mapping no EF Core.

Ao mesmo tempo, índices dos hot paths são priorizados para disponibilidade, agenda e pagamentos.

## Índices prioritários iniciais
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
payments(tenant_id,appointment_id,payment_purpose)
payment_attempts(tenant_id,payment_id,created_at)
payment_attempts(provider,provider_payment_id) UNIQUE WHERE provider_payment_id IS NOT NULL
refunds(tenant_id,payment_id)
```

Esta lista é baseline, não obrigação de criar índices redundantes. Validar com planos de execução e métricas conforme os hot paths reais aparecerem.

## Multi-tenancy e privacidade
TenantId do contexto autenticado, Global Query Filters EF Core, validações tenant-aware, índices tenant-scoped e testes de isolamento. RLS é evolução futura. LGPD detalhada em `docs/architecture/privacy-lgpd-v1.md`.

## Pontos antes da primeira migration
- validar unicidade/proteção CPF/CNPJ;
- definir retenção de Conversation/webhooks/logs;
- seed oficial de BusinessTypes;
- validar combo sem nesting;
- testes automatizados de isolamento, concorrência, hot paths e idempotência financeira.
