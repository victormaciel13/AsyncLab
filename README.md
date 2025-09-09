# ⚡ AsyncLab

## 🧪 Laboratório Async

### 🎯 Objetivo
Analisar o programa e tornar a sua execução **assíncrona**.

### 📝 Atividades
- 🔍 Identificar pontos do programa que podem ser transformados em chamadas assíncronas;  
- ⏱️ Observar o impacto no tempo de execução;  

### 📦 Entrega
  - 👥 **Geovanna Silva Cunha RM97736, Victor Camargo Maciel RM98384**;  
  - 🛠️ **Modificações implementadas**

Verificação do arquivo base antes do download.

Comparação entre base e arquivo novo, com geração de diffs (/diffs).

Exportação dos municípios por UF em CSV, JSON e BIN (/mun_por_uf).

Pesquisa interativa por UF, parte do nome ou código (IBGE/TOM) via console.

Tratamento automático de encoding (UTF-8 / Latin1).

Ajustes de compatibilidade e correção de erros de compilação;  
  - 📊 O tempo de execução é maior na primeira execução, pois envolve o download da base completa e a geração de todos os arquivos por UF (CSV, JSON e BIN), além das operações de hashing e escrita em disco. Em execuções seguintes, quando não há alterações no CSV, o impacto é bem menor, já que apenas a comparação entre arquivos é realizada, resultando em processamento mais rápido.  

### 🌐 Repositório
[https://github.com/profvinicius84/AsyncLab](https://github.com/victormaciel13/AsyncLab)


