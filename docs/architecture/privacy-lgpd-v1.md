# LGPD & Privacy Architecture v1

> Status: Draft v1 — requisito pré-implementação
> Data: 2026-09-15

## 1. Objetivo
Definir princípios e controles mínimos de privacidade e proteção de dados pessoais da Virtual Employee Platform antes da implementação. Este documento não substitui revisão jurídica.

## 2. Princípios
Privacy by Design e Privacy by Default: finalidade, adequação, necessidade/minimização, transparência, segurança, prevenção, não discriminação e responsabilização.

## 3. Papéis no tratamento
Controlador e Operador são definidos por finalidade/contexto. No atendimento do cliente final, estabelecimento/Tenant tende a ser Controlador e a plataforma Operador. A plataforma pode ser Controlador para cadastro/cobrança SaaS, segurança, prevenção de fraude, obrigações legais e gestão contratual.

## 4. Titulares e categorias de dados
Proprietário: nome, contato, CPF quando necessário, autenticação e dados contratuais/billing. Profissionais: nome, contato necessário, serviços/unidades, disponibilidade e agenda. Clientes finais: nome, WhatsApp, e-mail opcional, histórico de agendamentos, conteúdo conversacional necessário e referências de pagamento.

### Dados fiscais — aprovado
- `LegalEntity.Id` UUID; CPF/CNPJ nunca PK;
- documento normalizado/validado no backend;
- valor recuperável protegido em `DocumentEncrypted`;
- `DocumentFingerprint` via HMAC-SHA-256;
- unicidade tenant-scoped: `TenantId + DocumentType + DocumentFingerprint`;
- mesmo CPF/CNPJ pode existir em Tenants distintos sem vínculo ou exposição cross-tenant;
- não registrar documento integral em logs/traces/LLM; mascarar UI e auditar alterações sem replicar PII.

## 5. Inventário e finalidade
Antes do go-live: DataCategory, Purpose, DataSubject, ControllerRole, LegalBasisCandidate, Source, Storage, Recipients/Subprocessors, RetentionRule, DeletionOrAnonymizationRule e SecurityClassification. Base legal é avaliada por finalidade.

## 6. Minimização na IA
`Canal -> Conversation Engine -> Context Builder/Data Minimization -> AI Gateway -> LLM Provider`. Somente contexto necessário. Sem acesso direto do LLM ao banco; sem cartão, secrets ou PII fiscal por padrão.

## 7. Pagamentos
Sem dados brutos de cartão. Hosted/tokenized checkout -> Provider -> webhook validado -> backend. Persistir somente referências/estados necessários à operação, conciliação, auditoria e obrigações aplicáveis.

## 8. Segurança e isolamento
TenantId confiável, autorização server-side, Global Query Filters + validações tenant-aware, testes de isolamento, menor privilégio, secret manager, proteção em trânsito/repouso, redação de PII, rate limiting, webhook validation, idempotência/replay protection, audit trail e acesso administrativo auditável. RLS é evolução possível.

## 9. Retenção e eliminação — DECISÃO APROVADA

A política separa **conteúdo operacional/PII**, **fatos transacionais estruturados** e **histórico analítico**. Expirar conteúdo bruto não pode destruir fatos comerciais necessários à gestão do Tenant quando houver finalidade/base legítima para preservação.

### 9.1 Conteúdo de curta retenção
Conversations/WhatsApp, prompts/respostas LLM, payloads brutos de webhooks e logs técnicos detalhados terão retenção curta e diferenciada. Os prazos operacionais exatos serão configuráveis por categoria e validados antes do piloto conforme necessidade técnica, provedores e requisitos legais.

Após expurgo do conteúdo bruto, preservar apenas metadados técnicos mínimos quando necessários, como TenantId, ProviderEventId, EventType, timestamps, status, CorrelationId e hash/referência técnica, sem manter payload pessoal por conveniência.

### 9.2 Fatos comerciais e financeiros
Appointment, AppointmentItem, snapshots comerciais, Payment, PaymentAttempt e Refund possuem valor operacional, financeiro, histórico e analítico. Sua retenção segue finalidade comercial/contratual e obrigações aplicáveis, não o prazo curto de Conversation/Logs.

Snapshots de preço, duração e nome do serviço preservam a verdade histórica da transação mesmo que o cadastro atual do Service mude.

### 9.3 Analytics e histórico gerencial
O Dashboard deve manter capacidade de comparação histórica e tomada de decisão sem depender de conteúdo conversacional ou PII desnecessária.

Métricas previstas por Tenant/período, com filtros por Location quando aplicável:
- receita realizada e prevista;
- ticket médio;
- quantidade de appointments;
- cancelamentos, refunds e no-show;
- serviços mais utilizados e receita por serviço;
- desempenho de profissionais: atendimentos, receita, ticket médio, cancelamentos/no-show;
- clientes novos, recorrentes, ativos e inativos;
- última visita concluída do cliente;
- comparações entre períodos equivalentes;
- tendências por serviço/profissional/location.

Cliente inativo é derivado da última visita concluída e de um threshold configurável pelo Tenant (baseline de produto: 60 dias), evitando um `IsInactive` persistido que envelheça.

### 9.4 Separação entre PII e fatos analíticos
Quando juridicamente cabível, PII pode ser excluída/anonimizada sem necessariamente destruir fatos comerciais agregados ou históricos necessários à gestão. Analytics deve depender prioritariamente de dados estruturados e agregações, não de identidade pessoal ou texto de conversas.

No MVP, Analytics pode consultar o PostgreSQL transacional. Agregações/projeções diárias ou mensais são evolução orientada por volume/performance; não haverá Data Warehouse prematuro.

### 9.5 Regra central
> **Dados conversacionais e técnicos possuem retenção mínima necessária; fatos comerciais e financeiros preservam a capacidade de reconstruir o histórico do negócio; anonimização/expurgo de PII não deve destruir métricas legítimas do Tenant.**

Nenhum dado pessoal terá retenção indefinida apenas por conveniência. Prazos sujeitos a obrigação legal/regulatória devem ser validados antes do go-live.

## 10. Direitos do titular
Deve existir processo rastreável para acesso, correção, anonimização/bloqueio/eliminação quando aplicável, informação, revogação/oposição e demais direitos cabíveis. Não é obrigatório portal completo no MVP.

## 11. Solicitações de titulares
`PrivacyRequest` é modelo conceitual futuro; MVP pode começar por processo administrativo auditável.

## 12. Compartilhamento e suboperadores
Inventariar Meta/WhatsApp, LLM provider, gateway, billing SaaS e Azure/infraestrutura por finalidade, dados enviados, retenção, segurança e termos. Compartilhar somente o mínimo.

## 13. Observabilidade e logs
Preferir IDs internos, TenantId, LocationId, CorrelationId, EventId, códigos e estados. Evitar CPF/CNPJ, cartão, secrets, payload integral de WhatsApp e prompts/respostas completos. Conteúdo necessário para suporte tem acesso restrito e retenção curta definida.

## 14. Incidentes
Antes do piloto: procedimento de detecção, contenção, evidências, impacto, titulares/dados afetados, comunicação, avaliação jurídica e ações corretivas.

## 15. Requisitos para ERD e código
- TenantId em entidades tenant-owned;
- proteção/fingerprint fiscal tenant-scoped;
- sem UNIQUE global CPF/CNPJ;
- Customer não usa telefone como PK;
- snapshots mínimos preservam fatos comerciais;
- Analytics não depende de Conversation/PII para métricas históricas;
- expurgo de conteúdo bruto não remove automaticamente fatos transacionais legítimos;
- APIs minimizam PII;
- auditoria evita copiar PII.

## 16. Checklist pré-piloto
- [ ] Inventário de dados pessoais aprovado
- [ ] Finalidades/bases legais revisadas
- [ ] Papéis Controlador/Operador documentados
- [ ] Política de Privacidade e termos/DPA revisados
- [ ] Suboperadores registrados
- [x] Estratégia arquitetural de retenção definida
- [ ] Prazos operacionais/jurídicos finais por categoria validados antes do go-live
- [ ] Processo de direitos do titular testado
- [ ] Redação de PII em logs testada
- [ ] Isolamento multi-tenant testado
- [ ] Backup/restore e incidente testados/documentados
- [ ] AI Gateway validado para minimização
- [ ] Nenhum cartão bruto trafega pela aplicação

## 17. Decisão central
> **Coletar somente o necessário, proteger por padrão e separar identidade pessoal de fatos comerciais. O histórico analítico legítimo do Tenant deve sobreviver ao expurgo de conteúdo efêmero sem transformar Analytics em repositório de PII.**
