# LGPD & Privacy Architecture v1

> Status: Draft v1 — requisito pré-implementação
> Data: 2026-09-14

## 1. Objetivo

Definir os princípios e controles mínimos de privacidade e proteção de dados pessoais da Virtual Employee Platform antes da implementação.

Este documento não substitui revisão jurídica. Ele transforma requisitos de privacidade em decisões arquiteturais e operacionais verificáveis.

## 2. Princípios

A plataforma adota **Privacy by Design** e **Privacy by Default** como requisitos transversais.

Princípios aplicados:
- finalidade: cada dado precisa ter propósito definido;
- adequação: uso compatível com a finalidade informada;
- necessidade/minimização: coletar e expor somente o necessário;
- transparência: informar tratamento e compartilhamentos relevantes;
- segurança e prevenção: controles técnicos e organizacionais desde a concepção;
- não discriminação;
- responsabilização e prestação de contas.

Referências oficiais:
- LGPD — Lei nº 13.709/2018;
- ANPD — Titular de Dados: https://www.gov.br/anpd/pt-br/assuntos/titular-de-dados
- ANPD — Perguntas Frequentes: https://www.gov.br/anpd/pt-br/acesso-a-informacao/perguntas-frequentes

## 3. Papéis no tratamento

Os papéis de Controlador e Operador devem ser definidos por **finalidade e contexto**, não por uma regra única para toda a plataforma.

Cenário típico de atendimento do cliente final:

```text
Cliente final
   ↓
Estabelecimento / Tenant
   CONTROLADOR do relacionamento e agenda
   ↓
Virtual Employee Platform
   OPERADOR em tratamentos executados por instrução do estabelecimento
   ↓
Suboperadores / provedores
   Meta / WhatsApp
   LLM Provider
   Payment Provider
   Azure / infraestrutura
```

A plataforma poderá atuar como **Controlador** para finalidades próprias, por exemplo: cadastro e cobrança da assinatura SaaS, segurança da própria plataforma, prevenção de fraude, cumprimento de obrigações legais e gestão da relação contratual com o assinante.

A classificação final deve constar em contratos, política de privacidade e inventário de tratamento.

## 4. Titulares e categorias de dados

### 4.1 Proprietário / usuário do estabelecimento
Possíveis dados:
- nome;
- e-mail;
- telefone;
- CPF quando pessoa física;
- dados de autenticação e autorização;
- dados contratuais e de billing.

### 4.2 Profissionais
Possíveis dados:
- nome;
- telefone/e-mail quando necessário;
- vínculo com serviços e unidades;
- disponibilidade e agenda profissional.

### 4.3 Clientes finais
Possíveis dados:
- nome;
- telefone/WhatsApp;
- e-mail opcional;
- histórico de agendamentos;
- conteúdo conversacional necessário para atendimento;
- identificadores e estado de pagamento retornados pelo gateway.

### 4.4 Dados fiscais
`LegalEntity` pode armazenar CPF ou CNPJ. CPF é dado pessoal quando relacionado a pessoa natural.

Regras:
- armazenar documento normalizado;
- nunca usar CPF/CNPJ como chave primária;
- nunca registrar documento completo em logs/traces;
- mascarar na UI quando a visualização integral não for necessária;
- auditar alterações;
- restringir acesso por autorização;
- não enviar CPF/CNPJ ao LLM sem necessidade explícita.

## 5. Inventário e finalidade

Antes do go-live deve existir um inventário de tratamento contendo, no mínimo:

```text
DataCategory
Purpose
DataSubject
ControllerRole
LegalBasisCandidate
Source
Storage
Recipients/Subprocessors
RetentionRule
DeletionOrAnonymizationRule
SecurityClassification
```

A base legal deve ser validada por finalidade. **Consentimento não é a base padrão para todo tratamento.** Dependendo do contexto podem existir outras hipóteses previstas na LGPD, como execução de contrato, obrigação legal/regulatória ou legítimo interesse, quando aplicáveis e devidamente avaliadas.

## 6. Minimização de dados na IA

Regra arquitetural:

```text
WhatsApp / PWA
   ↓
Conversation Engine
   ↓
Context Builder + Data Minimization
   ↓
AI Gateway
   ↓
LLM Provider
```

O LLM recebe somente o contexto necessário para executar a tarefa.

Exemplo: para interpretar “quero corte amanhã às 15h”, o modelo pode receber intenção, serviço, data, contexto conversacional e identificadores técnicos mínimos. Não deve receber CPF/CNPJ, dados de cartão, histórico financeiro completo ou outros dados sem relação com a tarefa.

Proibições:
- LLM sem acesso direto ao banco;
- dados de cartão nunca enviados ao LLM;
- secrets/tokens nunca enviados ao LLM;
- logs de prompts/respostas não devem armazenar dados pessoais além do necessário;
- contexto conversacional deve possuir política de retenção.

## 7. Pagamentos

A plataforma não armazena dados brutos de cartão.

```text
Cliente
   ↓
Hosted / Tokenized Checkout
   ↓
Payment Provider
   ↓
Webhook validado
   ↓
Backend
```

A aplicação armazena apenas dados necessários à conciliação e estado da transação, por exemplo `provider_payment_id`, valor, método, status, timestamps e referências internas.

## 8. Segurança e isolamento

Controles mínimos:
- `TenantId` derivado de contexto confiável;
- autorização server-side;
- Global Query Filters EF Core + validações tenant-aware;
- testes automatizados de isolamento entre tenants;
- princípio do menor privilégio;
- secrets em secret manager;
- criptografia em trânsito;
- proteção de dados em repouso conforme serviço de infraestrutura;
- mascaramento/redação de PII em logs;
- rate limiting;
- validação de webhooks;
- idempotência e proteção contra replay;
- audit trail para operações críticas;
- acesso administrativo auditável.

RLS PostgreSQL é evolução possível e não substitui os controles da aplicação.

## 9. Retenção e eliminação

Nenhum dado pessoal deve possuir retenção indefinida apenas por conveniência.

Cada categoria deve ter política definida considerando:
- finalidade operacional;
- contrato;
- obrigação legal/regulatória aplicável;
- prevenção/exercício de direitos;
- necessidade de auditoria;
- minimização.

Estratégias possíveis:
- exclusão física quando permitida e segura;
- anonimização irreversível quando o valor estatístico puder ser preservado;
- soft-delete somente quando houver motivo legítimo para retenção;
- retenção diferenciada para Conversations, Webhook payloads e logs.

Pedido de exclusão não implica exclusão automática de tudo: antes da execução o sistema/processo deve avaliar eventual obrigação ou necessidade legítima de conservação.

## 10. Direitos do titular

A arquitetura não pode impedir o atendimento de direitos previstos pela LGPD.

O MVP deve permitir processo operacional para:
- confirmação da existência de tratamento;
- acesso;
- correção;
- anonimização/bloqueio/eliminação quando aplicável;
- informação sobre compartilhamento;
- revogação de consentimento quando essa for a base utilizada;
- oposição/requisições aplicáveis;
- portabilidade quando regulamentação e contexto aplicável permitirem;
- revisão/informação sobre decisões automatizadas quando aplicável.

Não é necessário construir um portal completo de privacidade no primeiro MVP. É necessário existir um canal/processo rastreável e capacidade técnica para localizar, corrigir, exportar, anonimizar ou eliminar dados quando juridicamente cabível.

## 11. Solicitações de titulares

Modelo conceitual futuro/operacional:

```text
PrivacyRequest
- Id
- TenantId?
- DataSubjectType
- RequestType
- Status
- IdentityVerificationStatus
- RequestedAt
- DueAt
- CompletedAt
- Resolution
- AuditReference
```

Tipos:
`ACCESS`, `CORRECTION`, `DELETION`, `ANONYMIZATION`, `INFORMATION`, `CONSENT_REVOCATION`, `OTHER`.

No MVP isso pode ser implementado inicialmente por processo administrativo auditável antes de justificar uma entidade persistida específica.

## 12. Compartilhamento e suboperadores

Provedores externos devem ser inventariados com:
- finalidade;
- categorias de dados enviadas;
- região/local de processamento quando relevante;
- política de retenção disponível;
- controles de segurança;
- termos/DPA aplicáveis;
- mecanismo de exclusão/atendimento de direitos quando necessário.

Principais grupos previstos:
- Meta / WhatsApp Business Platform;
- provedor de LLM;
- gateway de pagamentos;
- provedor de billing SaaS;
- Azure/infraestrutura e observabilidade.

A integração deve enviar o **mínimo necessário** para cada provedor.

## 13. Observabilidade e logs

Logs devem ser úteis para operação sem virar repositório paralelo de PII.

Preferir:
- IDs internos;
- TenantId;
- LocationId;
- CorrelationId;
- EventId;
- códigos de erro;
- estados técnicos.

Evitar:
- CPF/CNPJ integral;
- número de cartão;
- tokens/secrets;
- payload integral de WhatsApp por padrão;
- conteúdo completo de prompts/respostas quando não necessário.

Quando conteúdo conversacional precisar ser armazenado para suporte/auditoria, deve ter acesso restrito e retenção definida.

## 14. Incidentes de segurança

Antes do piloto real deve existir procedimento de incidente contendo:
- detecção;
- contenção;
- preservação de evidências;
- avaliação do impacto;
- identificação de titulares/dados afetados;
- comunicação interna;
- análise de necessidade de comunicação ao controlador, titulares e/ou ANPD conforme regra aplicável;
- registro das decisões e ações corretivas.

A decisão jurídica de notificação não deve ser automatizada apenas por regra técnica.

## 15. Requisitos para o ERD e código

- entidades tenant-owned carregam `TenantId` quando aplicável;
- `LegalEntity.DocumentNumber` é protegido contra exposição indevida;
- `Location` mantém dados de endereço separados da identidade fiscal;
- Customer não usa telefone como PK;
- dados históricos necessários à integridade comercial usam snapshots mínimos;
- entidades não são duplicadas apenas para facilitar IA;
- APIs não retornam PII desnecessária;
- DTOs públicos devem ser menores que as entidades persistidas quando possível;
- endpoints administrativos usam autorização explícita por papel/escopo;
- auditoria registra ação, ator, alvo e timestamp, evitando copiar PII desnecessária.

## 16. Checklist pré-piloto

- [ ] Inventário de dados pessoais aprovado
- [ ] Finalidades e bases legais revisadas
- [ ] Papéis Controlador/Operador documentados por fluxo
- [ ] Política de Privacidade publicada
- [ ] Termos/DPA com assinantes revisados
- [ ] Lista de suboperadores/provedores registrada
- [ ] Retenção definida para Customers, Conversations, Logs, Webhooks e Audit
- [ ] Processo de direitos do titular testado
- [ ] Redação de PII em logs testada
- [ ] Isolamento multi-tenant testado
- [ ] Backup/restore testado
- [ ] Procedimento de incidente documentado
- [ ] Context Builder/AI Gateway validado para minimização
- [ ] Nenhum dado bruto de cartão trafega pela aplicação

## 17. Decisão central

> **Coletar somente o necessário, usar somente para finalidade definida, compartilhar somente o mínimo, proteger por padrão e manter capacidade de atender os direitos do titular.**
