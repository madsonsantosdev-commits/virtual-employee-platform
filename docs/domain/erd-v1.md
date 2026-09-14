# ERD Físico v1 — PostgreSQL

> Status: Draft v1 — alinhado às regras aprovadas
> Data: 2026-09-14

## Convenções
PK `uuid`; instantes `timestamptz`; horários recorrentes `time`; dinheiro `numeric(12,2)`; dados tenant-owned carregam `tenant_id`. Privacy by Design: evitar duplicação de PII e exposição em logs.

## 1. tenants
`tenants: id PK, name, status, created_at, activated_at?, suspended_at?`

## 2. business_types
Tipos SYSTEM globais e CUSTOM tenant-scoped.

## 3. businesses
Business representa negócio/marca; MVP 1 por Tenant.

## 4. legal_entities
Identidade fiscal PERSON|COMPANY, CPF|CNPJ; documento normalizado/validado, nunca PK e protegido contra exposição indevida.

## 5. locations
Unidade operacional com `tenant_id`, `business_id`, LegalEntity opcional, dados de endereço, timezone e status.

## 6. users
Usuários administrativos tenant-scoped.

## 7. services
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
```
Define onde o Service é oferecido. Overrides ficam preparados, mas desabilitados na UX/API inicial.

## 9. service_components
COMBO -> componentes SINGLE; sem nesting.

## 10. professionals
Professional pertence ao Business.

## 11. professional_locations
Relacionamento Professional x Location; define onde o profissional trabalha.

## 12. professional_services
Relacionamento Professional x Service; define capacidade, não unidade.

## 13. availability_rules
Agenda-base recorrente = Professional + Location.

## 14. schedule_blocks
Bloqueio de Location inteira ou Professional específico na Location.

## 15. customers
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
Snapshots comerciais de 1..N Services do Appointment.

## 18. appointment_history
Suporta estados, horários, profissionais e `previous_location_id` / `new_location_id` em reagendamento entre unidades.

## 19. payments
`Payment` representa a **obrigação financeira lógica** do Appointment, não uma tentativa específica no gateway.

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
CHECK(status IN ('PENDING','PROCESSING','CONFIRMED','FAILED','CANCELLED','REFUNDED'))
UNIQUE(tenant_id,appointment_id,payment_purpose)
```

Regras:
- `Payment.Amount == Appointment.TotalPriceSnapshot` para SERVICE no MVP;
- um Appointment possui um pagamento lógico de serviço;
- falha/expiração de uma tentativa não cria outra obrigação financeira;
- Payment pode possuir N PaymentAttempts;
- `CONFIRMED` somente após confirmação confiável do provider;
- Payment não armazena PAN, CVV ou dados brutos de cartão.

## 20. payment_attempts
`PaymentAttempt` representa cada tentativa concreta de cobrança realizada por um provider.

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
CHECK(payment_method IN ('PIX','CREDIT_CARD','DEBIT_CARD'))
CHECK(status IN ('CREATED','PENDING','PROCESSING','CONFIRMED','FAILED','EXPIRED','CANCELLED'))
UNIQUE(tenant_id,idempotency_key)
```

Criar índice/constraint único para `(provider, provider_payment_id)` quando `provider_payment_id IS NOT NULL`.

`checkout_reference` deve guardar somente referência/URL segura necessária ao fluxo e nunca dados brutos de cartão ou secrets.

Fluxo:
```text
Appointment PENDING
-> Payment PENDING
-> PaymentAttempt
-> Gateway
-> Webhook validado/deduplicado
-> PaymentAttempt CONFIRMED
-> Payment CONFIRMED
-> Appointment CONFIRMED
```

Se uma tentativa falhar/expirar, Payment pode permanecer PENDING e receber nova tentativa enquanto Appointment ainda estiver reservável.

Se confirmação chegar após Appointment EXPIRED, não reativar a agenda; registrar confirmação financeira e iniciar fluxo compensatório/refund integral.

## 21. refunds
Refund integral no MVP e ligado ao Payment. Guarda também `payment_attempt_id` que identifica a transação externa efetivamente confirmada.

```text
refunds
id uuid PK
tenant_id uuid NOT NULL
appointment_id uuid NOT NULL FK -> appointments.id
payment_id uuid NOT NULL FK -> payments.id
payment_attempt_id uuid NOT NULL FK -> payment_attempts.id
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

Nunca comunicar REFUNDED antes da confirmação do provider.

## 22. conversations
Estado conversacional tenant-scoped; conteúdo tem política própria de retenção/minimização.

## 23. subscriptions
Subscription SaaS tenant-scoped e separada de Customer Payments.

## 24. subscription_units
Associação explícita Subscription x Location para unidades faturáveis.

## 25. usage_records
Tenant-scoped; `location_id uuid NULL` pode atribuir consumo à unidade quando tecnicamente possível.

## 26. webhook_inbox
Deduplicação por Provider + ProviderEventId; payload com retenção definida. Webhook de pagamento resolve a tentativa pelo identificador externo do provider e processa transição idempotente.

## 27. outbox_messages
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

## Orquestração financeira
```text
Appointment
    1
    |
    1 Payment lógico (SERVICE no MVP)
    |
    N PaymentAttempts
       -> provider externo
       -> webhook

Payment CONFIRMED
    |
    0..N Refunds
       -> PaymentAttempt confirmado que originou a transação
```

A camada de aplicação orquestra as transições; gateway é autoridade do estado financeiro externo; Appointment não é confirmado por redirect/browser.

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
Appointment -> 0..1 Payment lógico SERVICE
Payment -> N PaymentAttempt
Payment -> 0..N Refund
Refund -> 1 PaymentAttempt confirmado
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
payments(tenant_id,appointment_id,payment_purpose)
payment_attempts(tenant_id,payment_id,created_at)
payment_attempts(provider,provider_payment_id) UNIQUE WHERE provider_payment_id IS NOT NULL
refunds(tenant_id,payment_id)
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
- validar unicidade/proteção CPF/CNPJ;
- definir retenção de Conversation/webhooks/logs;
- seed oficial de BusinessTypes;
- validar combo sem nesting;
- testes automatizados de isolamento, concorrência e idempotência financeira.
